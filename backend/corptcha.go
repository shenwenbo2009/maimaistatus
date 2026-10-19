// Corptcha 人机验证接入(主验证通道):前端 widget 验证通过后取一次性 token,
// 随 POST /api/report 以 X-Corptcha-Token 头提交,网关调用 Corptcha /v1/verify
// 服务端核验。本地 8 位数字验证码保留为降级通道(服务故障时回落),长期并存。
// 文档: https://docs.corptcha.com/guide/quickstart.html
package main

import (
	"bytes"
	"context"
	"encoding/json"
	"io"
	"log"
	"net/http"
	"os"
	"strings"
	"sync/atomic"
	"time"
)

const (
	// corptchaPurposeReport 上报场景标识(Q1 决议),须与 Corptcha 控制台配置一致
	corptchaPurposeReport = "report"
	// corptchaDefaultAPIBase Corptcha 验证服务地址(Q7 决议:代码常量,不设环境变量)
	corptchaDefaultAPIBase = "https://cpt-api.25y.cn"
	// corptchaVerifyPath token 核验接口路径
	corptchaVerifyPath = "/v1/verify"
	// corptchaCooldown 服务故障后的降级冷却时长(Q3 决议)
	corptchaCooldown = 30 * time.Second
	// corptchaVerifyTimeout 单次核验外呼超时(Q6 决议),客户端另有 5s 兜底
	corptchaVerifyTimeout = 4 * time.Second
)

// corptchaConfig Corptcha 接入配置。siteKey/secret 经环境变量注入,
// 两值齐备才启用(enabled);apiBase/purpose/timeout 为代码固定值,不受环境变量影响。
type corptchaConfig struct {
	siteKey string
	secret  string
	apiBase string
	purpose string
	timeout time.Duration
	enabled bool
}

// loadCorptchaConfig 读取 CORPTCHA_SITE_KEY / CORPTCHA_SECRET。
// 未配置时静默回落纯本地验证码模式(行为与基线一致),只打日志提示,不失败退出。
// Secret 永不出现在任何日志中。
func loadCorptchaConfig() corptchaConfig {
	siteKey := strings.TrimSpace(os.Getenv("CORPTCHA_SITE_KEY"))
	secret := strings.TrimSpace(os.Getenv("CORPTCHA_SECRET"))
	cc := corptchaConfig{
		siteKey: siteKey,
		secret:  secret,
		apiBase: corptchaDefaultAPIBase,
		purpose: corptchaPurposeReport,
		timeout: corptchaVerifyTimeout,
		enabled: siteKey != "" && secret != "",
	}
	switch {
	case cc.enabled:
		log.Printf("corptcha enabled: siteKey=%s verify=%s%s", cc.siteKey, cc.apiBase, corptchaVerifyPath)
	case siteKey == "" && secret != "":
		log.Printf("corptcha disabled: CORPTCHA_SITE_KEY missing")
	case secret == "" && siteKey != "":
		log.Printf("corptcha disabled: CORPTCHA_SECRET missing")
	default:
		log.Printf("corptcha disabled: CORPTCHA_SITE_KEY / CORPTCHA_SECRET not configured")
	}
	return cc
}

// ---------- 降级状态(零协程方案) ----------

// corptchaDownUntil 故障到期时间戳(UnixNano,0=健康)。
// 调用契约(陷阱 T1):仅 verifyCorptchaToken 在真实外呼失败(Unavailable)时
// markCorptchaDown、真实外呼成功(OK)时 markCorptchaUp;冷却短路路径与 mode
// 查询只读不写——短路若续期,持续请求会把冷却变成永久降级。
var corptchaDownUntil atomic.Int64

func corptchaDegraded() bool { return time.Now().UnixNano() < corptchaDownUntil.Load() }

func markCorptchaDown() { corptchaDownUntil.Store(time.Now().Add(corptchaCooldown).UnixNano()) }

func markCorptchaUp() { corptchaDownUntil.Store(0) }

// ---------- verify 外呼 ----------

type verifyOutcome int

const (
	verifyOK          verifyOutcome = iota // 核验通过
	verifyRejected                         // 验证不通过(fail-closed,拒绝请求)
	verifyUnavailable                      // 服务不可用(降级本地验证码)
)

type verifyResult struct {
	outcome   verifyOutcome
	riskLevel string
	requestId string
	errorCode string
	reason    string
}

// corptchaHTTPClient 独立客户端(陷阱 T2):严禁复用全局 httpClient(12s 超时
// 会打穿 ≤5s 的核验时限约束),此处 5s 为兜底,实际由 ctx 超时(4s)先行截断。
var corptchaHTTPClient = &http.Client{Timeout: 5 * time.Second}

// corptchaVerifyResp 显式解析核验响应,未知字段忽略
type corptchaVerifyResp struct {
	Success   bool   `json:"success"`
	RiskLevel string `json:"riskLevel"`
	RequestID string `json:"requestId"`
	ErrorCode string `json:"errorCode"`
}

// verifyCorptchaToken 调用 Corptcha 核验接口验证一次性 token。
// ctx 必须以 r.Context() 为父(陷阱 T8:客户端断开即中止外呼,不得用 Background)。
// 结果三态:OK 放行 / Rejected 拒绝(403) / Unavailable 降级(503)。
// 不缓存任何核验结果,每次请求独立核验(BR-VERIFY-01)。
func verifyCorptchaToken(ctx context.Context, cfg corptchaConfig, token string) verifyResult {
	payload, err := json.Marshal(map[string]string{
		"token":   token,
		"purpose": cfg.purpose,
		"siteKey": cfg.siteKey,
	})
	if err != nil {
		return verifyResult{outcome: verifyUnavailable, reason: "marshal request"}
	}
	vctx, cancel := context.WithTimeout(ctx, cfg.timeout)
	defer cancel()
	req, err := http.NewRequestWithContext(vctx, http.MethodPost, cfg.apiBase+corptchaVerifyPath, bytes.NewReader(payload))
	if err != nil {
		return verifyResult{outcome: verifyUnavailable, reason: "build request"}
	}
	req.Header.Set("Content-Type", "application/json")
	req.Header.Set("Authorization", "Bearer "+cfg.secret)
	resp, err := corptchaHTTPClient.Do(req)
	if err != nil {
		markCorptchaDown()
		return verifyResult{outcome: verifyUnavailable, reason: "network"}
	}
	defer resp.Body.Close()
	var vr corptchaVerifyResp
	if err := json.NewDecoder(io.LimitReader(resp.Body, 128<<10)).Decode(&vr); err != nil {
		markCorptchaDown()
		return verifyResult{outcome: verifyUnavailable, reason: "bad response"}
	}
	switch {
	case resp.StatusCode == 200 && vr.Success:
		markCorptchaUp()
		// riskLevel 仅记录日志不作拒绝依据(Q2 决议)
		log.Printf("corptcha verify ok: risk=%s request=%s", vr.RiskLevel, vr.RequestID)
		return verifyResult{outcome: verifyOK, riskLevel: vr.RiskLevel, requestId: vr.RequestID}
	case resp.StatusCode == 200:
		// 200 但 success!=true:保守 fail-closed 拒绝
		log.Printf("corptcha verify rejected (success=false): request=%s", vr.RequestID)
		return verifyResult{outcome: verifyRejected, errorCode: vr.ErrorCode, requestId: vr.RequestID}
	case resp.StatusCode >= 400 && resp.StatusCode < 500:
		if vr.ErrorCode == "UNAUTHORIZED" {
			// Secret 错误属站点配置故障:降级不惩罚用户,并告警站长(BR-VERIFY-07)
			markCorptchaDown()
			log.Printf("corptcha unauthorized: check CORPTCHA_SECRET (request=%s)", vr.RequestID)
			return verifyResult{outcome: verifyUnavailable, errorCode: vr.ErrorCode, requestId: vr.RequestID, reason: "unauthorized"}
		}
		// TOKEN_NOT_FOUND / TOKEN_EXPIRED / TOKEN_PURPOSE_MISMATCH 及未知 errorCode:
		// 一律验证不通过(Rejected),不动降级冷却
		log.Printf("corptcha verify rejected: code=%s request=%s", vr.ErrorCode, vr.RequestID)
		return verifyResult{outcome: verifyRejected, errorCode: vr.ErrorCode, requestId: vr.RequestID}
	default:
		// 5xx 等其他状态:服务侧故障,降级
		markCorptchaDown()
		return verifyResult{outcome: verifyUnavailable, reason: "upstream status"}
	}
}

// handleReportMode 验证方式协商端点(BR-CPTCHA-02):前端进入验证步先查此接口。
// 纯内存原子读取,无副作用:不消耗凭据、不产生外呼、不触碰降级状态。
// purpose 恒为后端常量,构成单一事实源(Q1/Q8 决议)。
func handleReportMode(cfg corptchaConfig) http.HandlerFunc {
	return func(w http.ResponseWriter, r *http.Request) {
		mode, degraded := "local", false
		if cfg.enabled {
			if corptchaDegraded() {
				degraded = true
			} else {
				mode = "corptcha"
			}
		}
		writeJSON(w, 200, map[string]any{"mode": mode, "degraded": degraded, "purpose": corptchaPurposeReport})
	}
}
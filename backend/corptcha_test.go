package main

// Corptcha 接入单元测试(T-01~T-17,T-18 为 bugfix_test.go 的既有回归):
// fake verify server + fake bot-post 上游 + 直调 handleReport。
// 全局降级状态(corptchaDownUntil)为包级变量,所有用例 t.Cleanup(markCorptchaUp)
// 复位,避免用例顺序耦合(陷阱 T7);模拟冷却过期写过去时间戳,不等真实 30s。

import (
	"bytes"
	"io"
	"net/http"
	"net/http/httptest"
	"strings"
	"sync"
	"testing"
	"time"
)

// ---------- 测试基础设施 ----------

// fakeVerify 可编程 Corptcha 核验服务:记录请求计数与内容,可设返回状态码/响应体/延迟/自定义行为
type fakeVerify struct {
	mu      sync.Mutex
	count   int
	auths   []string
	bodies  []string
	status  int
	resp    string
	delay   time.Duration
	handler http.HandlerFunc
	url     string
}

func newFakeVerify(t *testing.T) *fakeVerify {
	t.Helper()
	fv := &fakeVerify{status: 200, resp: `{"success":true,"riskLevel":"low","requestId":"r1"}`}
	srv := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		fv.mu.Lock()
		fv.count++
		fv.auths = append(fv.auths, r.Header.Get("Authorization"))
		b, _ := io.ReadAll(r.Body)
		fv.bodies = append(fv.bodies, string(b))
		st, body, d, h := fv.status, fv.resp, fv.delay, fv.handler
		fv.mu.Unlock()
		if h != nil {
			h(w, r)
			return
		}
		if d > 0 {
			time.Sleep(d)
		}
		w.Header().Set("Content-Type", "application/json")
		w.WriteHeader(st)
		io.WriteString(w, body)
	}))
	t.Cleanup(srv.Close)
	fv.url = srv.URL
	return fv
}

func (fv *fakeVerify) set(status int, resp string) {
	fv.mu.Lock()
	fv.status, fv.resp = status, resp
	fv.mu.Unlock()
}

func (fv *fakeVerify) setDelay(d time.Duration) {
	fv.mu.Lock()
	fv.delay = d
	fv.mu.Unlock()
}

func (fv *fakeVerify) setHandler(h http.HandlerFunc) {
	fv.mu.Lock()
	fv.handler = h
	fv.mu.Unlock()
}

func (fv *fakeVerify) stats() (count int, auths, bodies []string) {
	fv.mu.Lock()
	defer fv.mu.Unlock()
	return fv.count, fv.auths, fv.bodies
}

// newFakeUpstream 可达的 fake bot-post 上游(透传目标)
func newFakeUpstream(t *testing.T) *httptest.Server {
	t.Helper()
	srv := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.URL.Path != "/bot-post" {
			w.WriteHeader(http.StatusNotFound)
			return
		}
		w.Header().Set("Content-Type", "application/json")
		w.WriteHeader(200)
		io.WriteString(w, `{"ok":true}`)
	}))
	t.Cleanup(srv.Close)
	return srv
}

// testCC 直构启用态配置,apiBase 指向 fake verify,超时可注短值
func testCC(apiBase string, timeout time.Duration) corptchaConfig {
	return corptchaConfig{
		siteKey: "cpt_test", secret: "test-secret",
		apiBase: apiBase, purpose: corptchaPurposeReport,
		timeout: timeout, enabled: true,
	}
}

const reportValidBody = `[{"t":103,"v":1,"r":"WEB"}]`

// reportReq 构造带三签名头与合法 body 的上报请求
func reportReq(body string) *http.Request {
	req := httptest.NewRequest(http.MethodPost, "/api/report", strings.NewReader(body))
	req.Header.Set("X-Client-ID", "test")
	req.Header.Set("X-Timestamp", "0")
	req.Header.Set("X-Signature", "sig")
	req.Header.Set("Content-Type", "application/json")
	return req
}

// ---------- T-01~T-06、T-15、T-16:verify 通道与外呼 ----------

// T-01 verify 成功放行:透传上游 200,核验请求契约(Bearer/token/purpose/siteKey)完整
func TestCorptchaVerifyOk(t *testing.T) {
	t.Cleanup(markCorptchaUp)
	fv := newFakeVerify(t)
	up := newFakeUpstream(t)
	cc := testCC(fv.url, 2*time.Second)
	req := reportReq(reportValidBody)
	req.Header.Set("X-Corptcha-Token", "tok-1")
	w := httptest.NewRecorder()
	handleReport(up.URL, cc, w, req)
	if w.Code != 200 {
		t.Fatalf("code=%d want 200 (body=%s)", w.Code, w.Body.String())
	}
	if !strings.Contains(w.Body.String(), `"ok":true`) {
		t.Fatalf("body=%q want upstream passthrough", w.Body.String())
	}
	count, auths, bodies := fv.stats()
	if count != 1 {
		t.Fatalf("verify called %d times, want 1", count)
	}
	if auths[0] != "Bearer test-secret" {
		t.Fatalf("authorization=%q want Bearer secret", auths[0])
	}
	for _, want := range []string{`"token":"tok-1"`, `"purpose":"report"`, `"siteKey":"cpt_test"`} {
		if !strings.Contains(bodies[0], want) {
			t.Fatalf("verify body=%q missing %s", bodies[0], want)
		}
	}
}

// T-02 token 重放拒绝:同一 token 第二次核验 TOKEN_NOT_FOUND → 403
func TestCorptchaTokenReplayRejected(t *testing.T) {
	t.Cleanup(markCorptchaUp)
	up := newFakeUpstream(t)
	n := 0
	fv := newFakeVerify(t)
	fv.setHandler(func(w http.ResponseWriter, r *http.Request) {
		n++
		if n == 1 {
			io.WriteString(w, `{"success":true,"requestId":"r1"}`)
			return
		}
		w.WriteHeader(400)
		io.WriteString(w, `{"success":false,"errorCode":"TOKEN_NOT_FOUND","requestId":"r2"}`)
	})
	cc := testCC(fv.url, 2*time.Second)
	for i, want := range []int{200, 403} {
		req := reportReq(reportValidBody)
		req.Header.Set("X-Corptcha-Token", "tok-same")
		w := httptest.NewRecorder()
		handleReport(up.URL, cc, w, req)
		if w.Code != want {
			t.Fatalf("attempt %d: code=%d want %d", i+1, w.Code, want)
		}
	}
}

// T-03 4xx TOKEN_* 全枚举拒绝:403 含 detail,且不进入降级(mode 仍 corptcha)
func TestCorptchaTokenErrsRejected(t *testing.T) {
	t.Cleanup(markCorptchaUp)
	for _, ec := range []string{"TOKEN_NOT_FOUND", "TOKEN_EXPIRED", "TOKEN_PURPOSE_MISMATCH"} {
		markCorptchaUp()
		fv := newFakeVerify(t)
		fv.set(400, `{"success":false,"errorCode":"`+ec+`","requestId":"r"}`)
		cc := testCC(fv.url, 2*time.Second)
		req := reportReq(reportValidBody)
		req.Header.Set("X-Corptcha-Token", "tok")
		w := httptest.NewRecorder()
		handleReport("http://127.0.0.1:1", cc, w, req)
		if w.Code != 403 {
			t.Fatalf("%s: code=%d want 403 (fail-closed, not degrade)", ec, w.Code)
		}
		if !strings.Contains(w.Body.String(), ec) {
			t.Fatalf("%s: body=%q missing errorCode detail", ec, w.Body.String())
		}
		if corptchaDegraded() {
			t.Fatalf("%s: must NOT enter degraded state", ec)
		}
	}
}

// T-04 verify 超时降级:503 + fallback 指引,耗时不超过注入超时+余量(独立 client,陷阱 T2)
func TestCorptchaVerifyTimeoutDegrades(t *testing.T) {
	t.Cleanup(markCorptchaUp)
	fv := newFakeVerify(t)
	fv.setDelay(500 * time.Millisecond)
	cc := testCC(fv.url, 50*time.Millisecond)
	req := reportReq(reportValidBody)
	req.Header.Set("X-Corptcha-Token", "tok")
	w := httptest.NewRecorder()
	start := time.Now()
	handleReport("http://127.0.0.1:1", cc, w, req)
	elapsed := time.Since(start)
	if w.Code != 503 {
		t.Fatalf("code=%d want 503 (body=%s)", w.Code, w.Body.String())
	}
	if !strings.Contains(w.Body.String(), `"fallback":"captcha"`) {
		t.Fatalf("body=%q want fallback hint", w.Body.String())
	}
	if elapsed > 2*time.Second {
		t.Fatalf("elapsed=%s exceeds timeout+margin (independent 4s-class client broken?)", elapsed)
	}
	if !corptchaDegraded() {
		t.Fatal("timeout must mark degraded")
	}
}

// T-05 verify 5xx 降级:服务侧故障 → 503 降级指引
func TestCorptchaVerify5xxDegrades(t *testing.T) {
	t.Cleanup(markCorptchaUp)
	fv := newFakeVerify(t)
	fv.set(503, `{"error":"boom"}`)
	cc := testCC(fv.url, 2*time.Second)
	req := reportReq(reportValidBody)
	req.Header.Set("X-Corptcha-Token", "tok")
	w := httptest.NewRecorder()
	handleReport("http://127.0.0.1:1", cc, w, req)
	if w.Code != 503 {
		t.Fatalf("code=%d want 503", w.Code)
	}
	if !strings.Contains(w.Body.String(), `"fallback":"captcha"`) {
		t.Fatalf("body=%q want fallback hint", w.Body.String())
	}
	if !corptchaDegraded() {
		t.Fatal("5xx must mark degraded")
	}
}

// T-06 UNAUTHORIZED 降级+告警:Secret 错误属站点配置故障 → 503(非 403),降级置位
func TestCorptchaUnauthorizedDegrades(t *testing.T) {
	t.Cleanup(markCorptchaUp)
	fv := newFakeVerify(t)
	fv.set(401, `{"success":false,"errorCode":"UNAUTHORIZED","requestId":"r"}`)
	cc := testCC(fv.url, 2*time.Second)
	req := reportReq(reportValidBody)
	req.Header.Set("X-Corptcha-Token", "tok")
	w := httptest.NewRecorder()
	handleReport("http://127.0.0.1:1", cc, w, req)
	if w.Code != 503 {
		t.Fatalf("code=%d want 503 (config fault degrades, not 403 punishing user)", w.Code)
	}
	if !corptchaDegraded() {
		t.Fatal("UNAUTHORIZED must mark degraded")
	}
}

// T-15 200+success!=true 拒绝:唯一成功判定是 200 且 success=true
func TestCorptcha200NotSuccessRejected(t *testing.T) {
	t.Cleanup(markCorptchaUp)
	fv := newFakeVerify(t)
	fv.set(200, `{"success":false,"errorCode":"TOKEN_NOT_FOUND","requestId":"r"}`)
	cc := testCC(fv.url, 2*time.Second)
	req := reportReq(reportValidBody)
	req.Header.Set("X-Corptcha-Token", "tok")
	w := httptest.NewRecorder()
	handleReport("http://127.0.0.1:1", cc, w, req)
	if w.Code != 403 {
		t.Fatalf("code=%d want 403 (fail-closed on success!=true)", w.Code)
	}
}

// T-16 未知 errorCode 拒绝:保守 fail-closed,不降级
func TestCorptchaUnknownErrorCodeRejected(t *testing.T) {
	t.Cleanup(markCorptchaUp)
	fv := newFakeVerify(t)
	fv.set(400, `{"success":false,"errorCode":"SOMETHING_NEW","requestId":"r"}`)
	cc := testCC(fv.url, 2*time.Second)
	req := reportReq(reportValidBody)
	req.Header.Set("X-Corptcha-Token", "tok")
	w := httptest.NewRecorder()
	handleReport("http://127.0.0.1:1", cc, w, req)
	if w.Code != 403 {
		t.Fatalf("code=%d want 403 (conservative fail-closed on unknown errorCode)", w.Code)
	}
	if corptchaDegraded() {
		t.Fatal("unknown errorCode must not degrade")
	}
}

// ---------- T-07~T-11、T-17:凭据分支与本地通道 ----------

// T-07 未配置走本地:零值 config,领码正确码通过/错误码 403,fake verify 零调用
func TestCorptchaUnconfiguredUsesLocal(t *testing.T) {
	t.Cleanup(markCorptchaUp)
	fv := newFakeVerify(t)
	up := newFakeUpstream(t)
	cc := corptchaConfig{} // 未启用
	// 正确码 → 透传成功
	id, code, err := newCaptcha()
	if err != nil {
		t.Fatal(err)
	}
	req := reportReq(reportValidBody)
	req.Header.Set("X-Captcha-ID", id)
	req.Header.Set("X-Captcha-Code", code)
	w := httptest.NewRecorder()
	handleReport(up.URL, cc, w, req)
	if w.Code != 200 {
		t.Fatalf("valid local captcha: code=%d want 200 (body=%s)", w.Code, w.Body.String())
	}
	// 错误码 → 403(现状语义)
	id2, _, err := newCaptcha()
	if err != nil {
		t.Fatal(err)
	}
	req2 := reportReq(reportValidBody)
	req2.Header.Set("X-Captcha-ID", id2)
	req2.Header.Set("X-Captcha-Code", "00000000")
	w2 := httptest.NewRecorder()
	handleReport(up.URL, cc, w2, req2)
	if w2.Code != 403 {
		t.Fatalf("wrong local captcha: code=%d want 403", w2.Code)
	}
	if !strings.Contains(w2.Body.String(), "invalid or expired captcha") {
		t.Fatalf("body=%q want legacy wording", w2.Body.String())
	}
	// 全程无任何对 Corptcha 的调用
	if count, _, _ := fv.stats(); count != 0 {
		t.Fatalf("verify called %d times, want 0", count)
	}
}

// T-08 未配置但带 token:前后端配置不一致兜底 → 503 降级指引(不 403 不 400)
func TestCorptchaUnconfiguredTokenFallsBack(t *testing.T) {
	t.Cleanup(markCorptchaUp)
	fv := newFakeVerify(t)
	cc := corptchaConfig{} // 未启用
	req := reportReq(reportValidBody)
	req.Header.Set("X-Corptcha-Token", "tok")
	w := httptest.NewRecorder()
	handleReport("http://127.0.0.1:1", cc, w, req)
	if w.Code != 503 {
		t.Fatalf("code=%d want 503", w.Code)
	}
	if !strings.Contains(w.Body.String(), `"fallback":"captcha"`) {
		t.Fatalf("body=%q want fallback hint", w.Body.String())
	}
	if count, _, _ := fv.stats(); count != 0 {
		t.Fatalf("verify called %d times, want 0", count)
	}
}

// T-09 凭据全缺 400:错误信息包含三种凭据头名
func TestCorptchaNoCredentials400(t *testing.T) {
	t.Cleanup(markCorptchaUp)
	req := reportReq(reportValidBody) // 无任何凭据头
	w := httptest.NewRecorder()
	handleReport("http://127.0.0.1:1", testCC("http://127.0.0.1:1", time.Second), w, req)
	if w.Code != 400 {
		t.Fatalf("code=%d want 400", w.Code)
	}
	for _, h := range []string{"X-Corptcha-Token", "X-Captcha-ID", "X-Captcha-Code"} {
		if !strings.Contains(w.Body.String(), h) {
			t.Fatalf("body=%q missing header name %s", w.Body.String(), h)
		}
	}
}

// T-10 413 先于凭据消耗:2MB body + 有效 token → 413 且 verify 零调用(陷阱 T4 守护)
func TestCorptcha413BeforeCredentialSpend(t *testing.T) {
	t.Cleanup(markCorptchaUp)
	fv := newFakeVerify(t)
	big := bytes.Repeat([]byte("a"), 2<<20)
	req := httptest.NewRequest(http.MethodPost, "/api/report", bytes.NewReader(big))
	req.Header.Set("X-Client-ID", "test")
	req.Header.Set("X-Timestamp", "0")
	req.Header.Set("X-Signature", "sig")
	req.Header.Set("X-Corptcha-Token", "tok")
	w := httptest.NewRecorder()
	handleReport("http://127.0.0.1:1", testCC(fv.url, time.Second), w, req)
	if w.Code != http.StatusRequestEntityTooLarge {
		t.Fatalf("code=%d want 413", w.Code)
	}
	if count, _, _ := fv.stats(); count != 0 {
		t.Fatalf("verify called %d times, want 0 (oversized body must not consume token)", count)
	}
}

// T-11 双凭据同带走通道 A:token 优先 Corptcha 核验,本地码未被消耗
func TestCorptchaDualCredentialsTokenWins(t *testing.T) {
	t.Cleanup(markCorptchaUp)
	fv := newFakeVerify(t)
	up := newFakeUpstream(t)
	cc := testCC(fv.url, 2*time.Second)
	id, code, err := newCaptcha()
	if err != nil {
		t.Fatal(err)
	}
	// 同带 token + 本地两头 → 走 verify 通道,200
	req := reportReq(reportValidBody)
	req.Header.Set("X-Corptcha-Token", "tok")
	req.Header.Set("X-Captcha-ID", id)
	req.Header.Set("X-Captcha-Code", code)
	w := httptest.NewRecorder()
	handleReport(up.URL, cc, w, req)
	if w.Code != 200 {
		t.Fatalf("dual credentials: code=%d want 200 (body=%s)", w.Code, w.Body.String())
	}
	if count, _, _ := fv.stats(); count != 1 {
		t.Fatalf("verify called %d times, want 1 (token channel takes precedence)", count)
	}
	// 同一本地码随后单用仍成功:证明通道 A 未消耗本地码(本地校验点未被误改)
	req2 := reportReq(reportValidBody)
	req2.Header.Set("X-Captcha-ID", id)
	req2.Header.Set("X-Captcha-Code", code)
	w2 := httptest.NewRecorder()
	handleReport(up.URL, cc, w2, req2)
	if w2.Code != 200 {
		t.Fatalf("local captcha reuse: code=%d want 200 (must not be consumed by token channel)", w2.Code)
	}
	if count, _, _ := fv.stats(); count != 1 {
		t.Fatalf("verify called %d times after local-only submit, still want 1", count)
	}
}

// T-17 坏 JSON 消耗语义维持:token 已核验但 body 非数组 → 400(现状顺序语义)
func TestCorptchaBadJsonAfterVerify(t *testing.T) {
	t.Cleanup(markCorptchaUp)
	fv := newFakeVerify(t)
	cc := testCC(fv.url, 2*time.Second)
	req := reportReq(`not-a-json-array`)
	req.Header.Set("X-Corptcha-Token", "tok")
	w := httptest.NewRecorder()
	handleReport("http://127.0.0.1:1", cc, w, req)
	if w.Code != 400 {
		t.Fatalf("code=%d want 400", w.Code)
	}
	if !strings.Contains(w.Body.String(), "JSON array") {
		t.Fatalf("body=%q want array wording", w.Body.String())
	}
	if count, _, _ := fv.stats(); count != 1 {
		t.Fatalf("verify called %d times, want 1 (credential consumed before JSON parse, legacy semantics)", count)
	}
}

// ---------- T-12~T-14:mode 协商与降级状态 ----------

func modeRequest() *http.Request {
	return httptest.NewRequest(http.MethodGet, "/api/report/mode", nil)
}

// T-12 mode 三态:未配置 local/false;启用+健康 corptcha/false;启用+降级 local/true;purpose 恒 report
func TestCorptchaModeNegotiation(t *testing.T) {
	t.Cleanup(markCorptchaUp)
	// 未配置
	w := httptest.NewRecorder()
	handleReportMode(corptchaConfig{})(w, modeRequest())
	if w.Code != 200 || !strings.Contains(w.Body.String(), `"mode":"local"`) || !strings.Contains(w.Body.String(), `"degraded":false`) {
		t.Fatalf("unconfigured: code=%d body=%s", w.Code, w.Body.String())
	}
	if !strings.Contains(w.Body.String(), `"purpose":"report"`) {
		t.Fatalf("purpose must be constant 'report', body=%s", w.Body.String())
	}
	// 启用 + 健康
	w2 := httptest.NewRecorder()
	handleReportMode(testCC("http://127.0.0.1:1", time.Second))(w2, modeRequest())
	if !strings.Contains(w2.Body.String(), `"mode":"corptcha"`) || !strings.Contains(w2.Body.String(), `"degraded":false`) {
		t.Fatalf("enabled+healthy: body=%s", w2.Body.String())
	}
	if !strings.Contains(w2.Body.String(), `"purpose":"report"`) {
		t.Fatalf("purpose must be constant 'report', body=%s", w2.Body.String())
	}
	// 启用 + 降级
	markCorptchaDown()
	w3 := httptest.NewRecorder()
	handleReportMode(testCC("http://127.0.0.1:1", time.Second))(w3, modeRequest())
	if !strings.Contains(w3.Body.String(), `"mode":"local"`) || !strings.Contains(w3.Body.String(), `"degraded":true`) {
		t.Fatalf("enabled+degraded: body=%s", w3.Body.String())
	}
	if !strings.Contains(w3.Body.String(), `"purpose":"report"`) {
		t.Fatalf("purpose must be constant 'report', body=%s", w3.Body.String())
	}
}

// T-13 冷却短路不外呼:降级期内带 token 请求 503,verify 零调用且不续期(陷阱 T1 守护)
func TestCorptchaCooldownShortCircuit(t *testing.T) {
	t.Cleanup(markCorptchaUp)
	fv := newFakeVerify(t)
	cc := testCC(fv.url, 2*time.Second)
	markCorptchaDown()
	downBefore := corptchaDownUntil.Load()
	req := reportReq(reportValidBody)
	req.Header.Set("X-Corptcha-Token", "tok")
	w := httptest.NewRecorder()
	handleReport("http://127.0.0.1:1", cc, w, req)
	if w.Code != 503 {
		t.Fatalf("code=%d want 503 (short-circuit during cooldown)", w.Code)
	}
	if count, _, _ := fv.stats(); count != 0 {
		t.Fatalf("verify called %d times, want 0 (short-circuit must not call out)", count)
	}
	if got := corptchaDownUntil.Load(); got != downBefore {
		t.Fatalf("cooldown was extended by short-circuit (permanent degradation risk): before=%d after=%d", downBefore, got)
	}
}

// T-14 冷却到期自动恢复:过期时间戳下带 token 请求真实外呼,成功后降级状态清零
func TestCorptchaCooldownExpiryRecovers(t *testing.T) {
	t.Cleanup(markCorptchaUp)
	fv := newFakeVerify(t)
	up := newFakeUpstream(t)
	cc := testCC(fv.url, 2*time.Second)
	// 模拟冷却已过期:写过期时间戳,不等真实 30s
	corptchaDownUntil.Store(time.Now().Add(-time.Second).UnixNano())
	req := reportReq(reportValidBody)
	req.Header.Set("X-Corptcha-Token", "tok")
	w := httptest.NewRecorder()
	handleReport(up.URL, cc, w, req)
	if w.Code != 200 {
		t.Fatalf("code=%d want 200 (expired cooldown must probe real verify)", w.Code)
	}
	if count, _, _ := fv.stats(); count != 1 {
		t.Fatalf("verify called %d times, want 1 (probe after cooldown expiry)", count)
	}
	if corptchaDegraded() {
		t.Fatal("successful verify must clear degraded state")
	}
	w2 := httptest.NewRecorder()
	handleReportMode(cc)(w2, modeRequest())
	if !strings.Contains(w2.Body.String(), `"mode":"corptcha"`) {
		t.Fatalf("mode after recovery: body=%s", w2.Body.String())
	}
}
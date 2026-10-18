// Maimai status gateway — Go 后端网关
//
// 职责:代理 + 缓存 isMaiDown 公开接口,定时采集状态并落盘历史(JSONL,无外部依赖,仅标准库)。
// 上游文档: https://mai.chongxi.us/docs
// 数据来源必须标注: 数据来源于 isMaiDown by Chongxi (https://mai.chongxi.us)
package main

import (
	"bufio"
	"bytes"
	"context"
	"crypto/rand"
	"crypto/subtle"
	_ "embed"
	"encoding/hex"
	"encoding/json"
	"fmt"
	"io"
	"log"
	"net/http"
	"os"
	"os/signal"
	"path/filepath"
	"sort"
	"strconv"
	"strings"
	"sync"
	"syscall"
	"time"
)

const attribution = "数据来源于 isMaiDown by Chongxi (https://mai.chongxi.us)"

//go:embed openapi.json
var openapiSpec []byte

// ---------- sse hub ----------

// hub 向所有 /api/stream 订阅者广播每次采集到的状态
type hub struct {
	mu   sync.Mutex
	subs map[chan []byte]struct{}
}

func newHub() *hub { return &hub{subs: make(map[chan []byte]struct{})} }

func (h *hub) add() chan []byte {
	ch := make(chan []byte, 4)
	h.mu.Lock()
	h.subs[ch] = struct{}{}
	h.mu.Unlock()
	return ch
}

func (h *hub) remove(ch chan []byte) {
	h.mu.Lock()
	delete(h.subs, ch)
	close(ch)
	h.mu.Unlock()
}

func (h *hub) broadcast(b []byte) {
	h.mu.Lock()
	defer h.mu.Unlock()
	for ch := range h.subs {
		select {
		case ch <- b:
		default: // 慢订阅者直接丢包,保证采集不受阻塞
		}
	}
}

// ---------- config ----------

type config struct {
	port           string
	upstreamBase   string // https://mai.chongxi.us
	reportUpstream string // https://maiapi.chongxi.us
	pollInterval   time.Duration
	dataDir        string
	historyMax     int
	corsAllow      string
}

func getenv(key, def string) string {
	if v := os.Getenv(key); v != "" {
		return v
	}
	return def
}

func loadConfig() config {
	secs, _ := strconv.Atoi(getenv("POLL_INTERVAL_SEC", "30"))
	if secs <= 0 {
		secs = 30
	}
	max, _ := strconv.Atoi(getenv("HISTORY_MAX", "20000"))
	if max <= 0 {
		max = 20000
	}
	return config{
		port:           getenv("PORT", "9560"),
		upstreamBase:   strings.TrimSuffix(getenv("UPSTREAM_BASE", "https://mai.chongxi.us"), "/"),
		reportUpstream: strings.TrimSuffix(getenv("REPORT_UPSTREAM", "https://maiapi.chongxi.us"), "/"),
		pollInterval:   time.Duration(secs) * time.Second,
		dataDir:        getenv("DATA_DIR", "./data"),
		historyMax:     max,
		corsAllow:      getenv("CORS_ALLOW", "*"),
	}
}

// ---------- ttl cache ----------

type cacheEntry struct {
	body      []byte
	status    int
	fetchedAt time.Time
	ttl       time.Duration
}

type cache struct {
	mu sync.RWMutex
	m  map[string]*cacheEntry
}

func newCache() *cache { return &cache{m: make(map[string]*cacheEntry)} }

func (c *cache) get(key string) (*cacheEntry, bool) {
	c.mu.RLock()
	e, ok := c.m[key]
	c.mu.RUnlock()
	if !ok {
		return nil, false
	}
	if time.Since(e.fetchedAt) > e.ttl {
		return nil, false
	}
	return e, true
}

func (c *cache) set(key string, e *cacheEntry) {
	c.mu.Lock()
	c.m[key] = e
	c.mu.Unlock()
}

// ---------- upstream fetch ----------

var httpClient = &http.Client{Timeout: 12 * time.Second}

func fetchUpstream(ctx context.Context, url string) (body []byte, status int, err error) {
	req, err := http.NewRequestWithContext(ctx, http.MethodGet, url, nil)
	if err != nil {
		return nil, 0, err
	}
	req.Header.Set("User-Agent", "maimai-gateway/1.0 (+https://mai.chongxi.us)")
	resp, err := httpClient.Do(req)
	if err != nil {
		return nil, 0, err
	}
	defer resp.Body.Close()
	b, err := io.ReadAll(io.LimitReader(resp.Body, 8<<20))
	if err != nil {
		return nil, 0, err
	}
	return b, resp.StatusCode, nil
}

// ---------- history store (JSONL) ----------

type historySvc struct {
	Key     string `json:"key"`
	State   string `json:"state"`
	Latency *int   `json:"latency_ms"`
}

type historyPoint struct {
	Ts          int64        `json:"ts"`
	Verdict     string       `json:"verdict"`
	VerdictText string       `json:"verdict_text"`
	LatencyMs   *int         `json:"latency_ms"`
	Anomaly     int          `json:"anomaly"`
	Normal      int          `json:"normal"`
	OkCount     int          `json:"ok_count"`
	DownCount   int          `json:"down_count"`
	Maint       bool         `json:"maint"`
	Services    []historySvc `json:"services,omitempty"`
}

// 服务 key → 中文名(历史点位只存 key,展示时还原)
var serviceNames = map[string]string{
	"net": "NET", "aime": "会员", "title": "标题",
	"member": "公众号(NET)", "qr": "公众号(二维码)", "bl": "计费",
}

func serviceName(key string) string {
	if n, ok := serviceNames[key]; ok {
		return n
	}
	return key
}

type botStatus struct {
	Verdict     string `json:"verdict"`
	VerdictText string `json:"verdict_text"`
	Latency     struct {
		CurrentMs *int `json:"current_ms"`
	} `json:"latency"`
	Reports struct {
		AnomalyCount int `json:"anomaly_count"`
		NormalCount  int `json:"normal_count"`
	} `json:"reports"`
	Services []struct {
		Key     string `json:"key"`
		State   string `json:"state"`
		Latency *int   `json:"latency"`
	} `json:"services"`
}

func historyPath(dataDir string) string { return filepath.Join(dataDir, "history.jsonl") }

var histMu sync.Mutex

func appendHistory(dataDir string, body []byte, max int) {
	var s botStatus
	if err := json.Unmarshal(body, &s); err != nil {
		return
	}
	ok, down := 0, 0
	svcs := make([]historySvc, 0, len(s.Services))
	for _, sv := range s.Services {
		if sv.State == "ok" {
			ok++
		} else if sv.State == "down" {
			down++
		}
		svcs = append(svcs, historySvc{Key: sv.Key, State: sv.State, Latency: sv.Latency})
	}
	p := historyPoint{
		Ts: time.Now().UnixMilli(), Verdict: s.Verdict, VerdictText: s.VerdictText,
		LatencyMs: s.Latency.CurrentMs, Anomaly: s.Reports.AnomalyCount,
		Normal: s.Reports.NormalCount, OkCount: ok, DownCount: down, Services: svcs,
	}
	p.Maint = isMaintPoint(p)
	line, _ := json.Marshal(p)

	histMu.Lock()
	defer histMu.Unlock()
	if err := os.MkdirAll(dataDir, 0o755); err != nil {
		log.Printf("mkdir data: %v", err)
		return
	}
	f, err := os.OpenFile(historyPath(dataDir), os.O_APPEND|os.O_CREATE|os.O_WRONLY, 0o644)
	if err != nil {
		log.Printf("open history: %v", err)
		return
	}
	f.Write(append(line, '\n'))
	f.Close()
	trimHistory(dataDir, max)
}

func trimHistory(dataDir string, max int) {
	p := historyPath(dataDir)
	f, err := os.Open(p)
	if err != nil {
		return
	}
	var lines []string
	sc := bufio.NewScanner(f)
	sc.Buffer(make([]byte, 1024*1024), 1024*1024)
	for sc.Scan() {
		lines = append(lines, sc.Text())
	}
	f.Close()
	if len(lines) <= max {
		return
	}
	lines = lines[len(lines)-max:]
	tmp := p + ".tmp"
	if err := os.WriteFile(tmp, []byte(strings.Join(lines, "\n")+"\n"), 0o644); err != nil {
		return
	}
	os.Rename(tmp, p)
}

func readHistory(dataDir string, limit int) []historyPoint {
	pts := readHistoryAsc(dataDir, 0)
	if limit > 0 && len(pts) > limit {
		pts = pts[len(pts)-limit:]
	}
	// 倒序返回(最新在前,前端好渲染)
	for i, j := 0, len(pts)-1; i < j; i, j = i+1, j-1 {
		pts[i], pts[j] = pts[j], pts[i]
	}
	return pts
}

// readHistoryAsc 按时间正序返回全部点位,供统计类接口使用
func readHistoryAsc(dataDir string, limit int) []historyPoint {
	histMu.Lock()
	defer histMu.Unlock()
	f, err := os.Open(historyPath(dataDir))
	if err != nil {
		return nil
	}
	defer f.Close()
	var all []historyPoint
	sc := bufio.NewScanner(f)
	sc.Buffer(make([]byte, 1024*1024), 1024*1024)
	for sc.Scan() {
		var p historyPoint
		if err := json.Unmarshal([]byte(sc.Text()), &p); err == nil {
			p.Maint = isMaintPoint(p) // 老数据补算
			all = append(all, p)
		}
	}
	if limit > 0 && len(all) > limit {
		all = all[len(all)-limit:]
	}
	return all
}

// ---------- 维护窗口(华立常规维护,北京时间每日) ----------
//
// 依据:国服玩家社区案例(维护结束约早 7 点)与 isMaiDown 夜间事件分布,
// 默认 04:00-07:00(Asia/Shanghai,中国无夏令时,用固定 UTC+8 即可)。
// 可用环境变量覆盖:MAINT_WINDOWS="04:00-07:00,23:30-00:30"(逗号分隔,支持跨日)。
// 维护点位不计入可用率;图表里置灰(柱满格灰 / 线压 0 灰)。
var maintWindows = parseMaintWindows(getenv("MAINT_WINDOWS", "04:00-07:00"))

func parseMaintWindows(s string) [][2]int {
	var out [][2]int
	toMin := func(t string) (int, bool) {
		parts := strings.Split(strings.TrimSpace(t), ":")
		if len(parts) != 2 {
			return 0, false
		}
		h, err1 := strconv.Atoi(parts[0])
		m, err2 := strconv.Atoi(parts[1])
		if err1 != nil || err2 != nil || h < 0 || h > 23 || m < 0 || m > 59 {
			return 0, false
		}
		return h*60 + m, true
	}
	for _, w := range strings.Split(s, ",") {
		b := strings.SplitN(w, "-", 2)
		if len(b) != 2 {
			continue
		}
		from, ok1 := toMin(b[0])
		to, ok2 := toMin(b[1])
		if ok1 && ok2 {
			out = append(out, [2]int{from, to})
		}
	}
	return out
}

// inRoutineMaint 判断毫秒时间戳是否落在每日维护窗口(北京时间)
func inRoutineMaint(tsMillis int64) bool {
	mins := int((tsMillis/60000 + 8*60) % 1440)
	for _, w := range maintWindows {
		from, to := w[0], w[1]
		if from <= to {
			if mins >= from && mins < to {
				return true
			}
		} else if mins >= from || mins < to { // 跨日窗口
			return true
		}
	}
	return false
}

// isMaintPoint 维护点位:上游 verdict 报 maintenance,或落在常规窗口
func isMaintPoint(p historyPoint) bool {
	return p.Verdict == "maintenance" || inRoutineMaint(p.Ts)
}

// maintSpans 把维护点合并为 [start,end] 毫秒区间(间隔超 10 分钟即断开)
func maintSpans(pts []historyPoint) [][2]int64 {
	var spans [][2]int64
	for _, p := range pts {
		if !isMaintPoint(p) {
			continue
		}
		if n := len(spans); n > 0 && p.Ts-spans[n-1][1] <= 10*60*1000 {
			spans[n-1][1] = p.Ts
		} else {
			spans = append(spans, [2]int64{p.Ts, p.Ts})
		}
	}
	return spans
}

// overallAvailability 近 hours 小时全服务总可用率(与 availability 聚合同源,维护点位剔除)
// 返回:百分比,总检查数,维护点数
func overallAvailability(dataDir string, hours int) (float64, int, int) {
	cutoff := time.Now().Add(-time.Duration(hours) * time.Hour).UnixMilli()
	ok, tot, maint := 0, 0, 0
	for _, p := range readHistoryAsc(dataDir, 0) {
		if p.Ts < cutoff || len(p.Services) == 0 {
			continue
		}
		if p.Maint {
			maint++
			continue
		}
		for _, s := range p.Services {
			tot++
			if s.State == "ok" {
				ok++
			}
		}
	}
	if tot == 0 {
		return 0, 0, maint
	}
	return float64(ok) / float64(tot) * 100, tot, maint
}

// historySummary 近 12h 可用率与平均延迟
// uptime_pct 与监控页服务可用性(近12h)同源:全服务检查数口径,维护点位剔除不计
func historySummary(dataDir string) map[string]any {
	pts := readHistoryAsc(dataDir, 1440)
	uptime, _, maint := overallAvailability(dataDir, 12)
	total, latSum, latN := 0, 0, 0
	var latest map[string]any
	for _, p := range pts {
		if p.Maint {
			continue
		}
		total++
		if p.LatencyMs != nil {
			latSum += *p.LatencyMs
			latN++
		}
		latest = map[string]any{"ts": p.Ts, "verdict": p.Verdict, "verdict_text": p.VerdictText}
	}
	avg := 0
	if latN > 0 {
		avg = latSum / latN
	}
	if latest == nil {
		latest = map[string]any{}
	}
	return map[string]any{
		"total": total, "uptime_pct": fmt.Sprintf("%.2f", uptime),
		"avg_latency_ms": avg, "latest": latest,
		"maint_points": maint, "window_hours": 12,
	}
}

var verdictRank = map[string]int{"normal": 0, "recovering": 1, "degraded": 2, "outage": 3, "maintenance": 4, "nodata": 5}

// incidents 从历史中提取异常连续段:verdict 非 normal 即视为事件
func incidents(dataDir string) []map[string]any {
	pts := readHistoryAsc(dataDir, 0)
	var out []map[string]any
	var cur map[string]any
	var curWorst string
	flush := func(end historyPoint) {
		if cur == nil {
			return
		}
		startTs := cur["start_ts"].(int64)
		cur["end_ts"] = end.Ts
		cur["duration_sec"] = (end.Ts - startTs) / 1000
		cur["worst_verdict"] = curWorst
		out = append(out, cur)
		cur = nil
	}
	for _, p := range pts {
		if p.Verdict == "" || p.Verdict == "normal" {
			if cur != nil {
				flush(p)
			}
			continue
		}
		if cur == nil {
			curWorst = p.Verdict
			cur = map[string]any{"start_ts": p.Ts, "verdict_text": p.VerdictText, "ongoing": false}
		} else if verdictRank[p.Verdict] > verdictRank[curWorst] {
			curWorst = p.Verdict
		}
		cur["end_ts"] = p.Ts
		cur["verdict_text"] = p.VerdictText
	}
	if cur != nil { // 最后一段仍在持续
		cur["duration_sec"] = (time.Now().UnixMilli() - cur["start_ts"].(int64)) / 1000
		cur["worst_verdict"] = curWorst
		cur["ongoing"] = true
		out = append(out, cur)
	}
	// 倒序
	for i, j := 0, len(out)-1; i < j; i, j = i+1, j-1 {
		out[i], out[j] = out[j], out[i]
	}
	return out
}

// latencyStats 指定窗口内的延迟分位统计,维护点位剔除不计
func latencyStats(dataDir string, hours int) map[string]any {
	if hours <= 0 || hours > 168 {
		hours = 24
	}
	cutoff := time.Now().Add(-time.Duration(hours) * time.Hour).UnixMilli()
	var lat []int
	current := 0
	for _, p := range readHistoryAsc(dataDir, 0) {
		if p.Ts < cutoff || p.LatencyMs == nil || p.Maint {
			continue
		}
		lat = append(lat, *p.LatencyMs)
		current = *p.LatencyMs
	}
	if len(lat) == 0 {
		return map[string]any{"hours": hours, "count": 0}
	}
	sort.Ints(lat)
	pct := func(q float64) int { return lat[int(q*float64(len(lat)-1))] }
	sum := 0
	for _, v := range lat {
		sum += v
	}
	return map[string]any{
		"hours": hours, "count": len(lat),
		"min": lat[0], "p50": pct(0.5), "p90": pct(0.9), "p99": pct(0.99),
		"max": lat[len(lat)-1], "avg": sum / len(lat), "current": current,
	}
}

// ---------- helpers ----------

func writeJSON(w http.ResponseWriter, status int, v any) {
	w.Header().Set("Content-Type", "application/json; charset=utf-8")
	w.Header().Set("X-Data-Source", attribution)
	w.WriteHeader(status)
	json.NewEncoder(w).Encode(v)
}

func writeUpstream(w http.ResponseWriter, body []byte, status int, hit bool) {
	w.Header().Set("Content-Type", "application/json; charset=utf-8")
	w.Header().Set("X-Data-Source", attribution)
	if hit {
		w.Header().Set("X-Cache", "HIT")
	} else {
		w.Header().Set("X-Cache", "MISS")
	}
	w.WriteHeader(status)
	w.Write(body)
}

func withCORS(corsAllow string, next http.Handler) http.Handler {
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("Access-Control-Allow-Origin", corsAllow)
		w.Header().Set("Access-Control-Allow-Methods", "GET, POST, OPTIONS")
		w.Header().Set("Access-Control-Allow-Headers", "Content-Type, X-Client-ID, X-Timestamp, X-Signature, X-Session")
		if r.Method == http.MethodOptions {
			w.WriteHeader(http.StatusNoContent)
			return
		}
		next.ServeHTTP(w, r)
	})
}

func proxyCached(c *cache, key, url string, ttl time.Duration) http.HandlerFunc {
	return func(w http.ResponseWriter, r *http.Request) {
		if e, ok := c.get(key); ok {
			writeUpstream(w, e.body, e.status, true)
			return
		}
		body, status, err := fetchUpstream(r.Context(), url)
		if err != nil {
			log.Printf("fetch %s: %v", url, err)
			writeJSON(w, http.StatusBadGateway, map[string]any{"error": "upstream unreachable", "detail": err.Error()})
			return
		}
		c.set(key, &cacheEntry{body: body, status: status, fetchedAt: time.Now(), ttl: ttl})
		writeUpstream(w, body, status, false)
	}
}

// ---------- 上报验证码(8 位数字,5 分钟有效,一次有效) ----------

type captchaEntry struct {
	code string
	exp  time.Time
}

var (
	captchaMu sync.Mutex
	captchas  = map[string]captchaEntry{}
)

func newCaptcha() (id, code string) {
	var raw [24]byte
	if _, err := rand.Read(raw[:]); err != nil {
		log.Printf("captcha rand: %v", err)
	}
	id = hex.EncodeToString(raw[:16])
	digits := make([]byte, 8)
	for i := 0; i < 8; i++ {
		digits[i] = '0' + raw[16+i]%10
	}
	code = string(digits)
	captchaMu.Lock()
	defer captchaMu.Unlock()
	now := time.Now()
	for k, e := range captchas { // 顺手清过期
		if now.After(e.exp) {
			delete(captchas, k)
		}
	}
	captchas[id] = captchaEntry{code: code, exp: now.Add(5 * time.Minute)}
	return id, code
}

func verifyCaptcha(id, code string) bool {
	captchaMu.Lock()
	defer captchaMu.Unlock()
	e, ok := captchas[id]
	if !ok || time.Now().After(e.exp) {
		delete(captchas, id)
		return false
	}
	if subtle.ConstantTimeCompare([]byte(e.code), []byte(code)) != 1 {
		return false
	}
	delete(captchas, id) // 一次有效
	return true
}

// ---------- main ----------

func main() {
	cfg := loadConfig()
	c := newCache()
	streamHub := newHub()
	mux := http.NewServeMux()

	mux.HandleFunc("GET /healthz", func(w http.ResponseWriter, r *http.Request) {
		writeJSON(w, 200, map[string]any{"ok": true, "time": time.Now().Format(time.RFC3339)})
	})
	// 同域部署时反代只放行 /api/*,加个别名让健康检查同口径
	mux.HandleFunc("GET /api/healthz", func(w http.ResponseWriter, r *http.Request) {
		writeJSON(w, 200, map[string]any{"ok": true, "time": time.Now().Format(time.RFC3339)})
	})

	// 实时状态(短缓存,高频轮询)
	mux.HandleFunc("GET /api/status", proxyCached(c, "status", cfg.upstreamBase+"/api/bot", 20*time.Second))
	// 兼容原路径,前端可直接切到本网关
	mux.HandleFunc("GET /api/bot", proxyCached(c, "status", cfg.upstreamBase+"/api/bot", 20*time.Second))

	// 静态快照(长缓存)
	mux.HandleFunc("GET /api/top100", proxyCached(c, "top100", cfg.upstreamBase+"/api/top100", time.Hour))
	mux.HandleFunc("GET /api/chart145", proxyCached(c, "chart145", cfg.upstreamBase+"/api/chart145", time.Hour))
	mux.HandleFunc("GET /api/ratinglist", func(w http.ResponseWriter, r *http.Request) {
		version := r.URL.Query().Get("version")
		key := "ratinglist:all"
		url := cfg.upstreamBase + "/api/ratinglist"
		if version != "" {
			// 简单白名单,防 SSRF 拼接
			allowed := map[string]bool{"dx2023": true, "dx2024": true, "dx2025": true, "dx2026": true}
			if !allowed[version] {
				writeJSON(w, 400, map[string]any{"error": "invalid version"})
				return
			}
			key = "ratinglist:" + version
			url += "?version=" + version
		}
		proxyCached(c, key, url, time.Hour).ServeHTTP(w, r)
	})

	// 历史(本地采集)
	mux.HandleFunc("GET /api/history", func(w http.ResponseWriter, r *http.Request) {
		limit, _ := strconv.Atoi(r.URL.Query().Get("limit"))
		if limit <= 0 || limit > 2000 {
			limit = 288
		}
		writeJSON(w, 200, map[string]any{"points": readHistory(cfg.dataDir, limit)})
	})
	mux.HandleFunc("GET /api/history/summary", func(w http.ResponseWriter, r *http.Request) {
		writeJSON(w, 200, historySummary(cfg.dataDir))
	})

	// 历史范围查询:from/to 为毫秒时间戳
	mux.HandleFunc("GET /api/history/range", func(w http.ResponseWriter, r *http.Request) {
		from, _ := strconv.ParseInt(r.URL.Query().Get("from"), 10, 64)
		to, _ := strconv.ParseInt(r.URL.Query().Get("to"), 10, 64)
		limit, _ := strconv.Atoi(r.URL.Query().Get("limit"))
		if limit <= 0 || limit > 5000 {
			limit = 2000
		}
		var out []historyPoint
		for _, p := range readHistory(cfg.dataDir, limit) {
			if from > 0 && p.Ts < from {
				continue
			}
			if to > 0 && p.Ts > to {
				continue
			}
			out = append(out, p)
		}
		if out == nil {
			out = []historyPoint{}
		}
		writeJSON(w, 200, map[string]any{"points": out})
	})

	// 故障事件:由历史自动提取的异常连续段
	mux.HandleFunc("GET /api/incidents", func(w http.ResponseWriter, r *http.Request) {
		list := incidents(cfg.dataDir)
		if list == nil {
			list = []map[string]any{}
		}
		writeJSON(w, 200, map[string]any{"incidents": list, "count": len(list)})
	})

	// 延迟分位:?hours=24(最大168)
	mux.HandleFunc("GET /api/latency/stats", func(w http.ResponseWriter, r *http.Request) {
		hours, _ := strconv.Atoi(r.URL.Query().Get("hours"))
		writeJSON(w, 200, latencyStats(cfg.dataDir, hours))
	})

	// 上报趋势:供前端画异常/正常上报曲线
	mux.HandleFunc("GET /api/reports/trend", func(w http.ResponseWriter, r *http.Request) {
		limit, _ := strconv.Atoi(r.URL.Query().Get("limit"))
		if limit <= 0 || limit > 2000 {
			limit = 288
		}
		pts := readHistory(cfg.dataDir, limit)
		trend := make([]map[string]any, 0, len(pts))
		for i := len(pts) - 1; i >= 0; i-- { // 正序返回,方便画线
			p := pts[i]
			trend = append(trend, map[string]any{"ts": p.Ts, "anomaly": p.Anomaly, "normal": p.Normal})
		}
		writeJSON(w, 200, map[string]any{"trend": trend})
	})

	// 各服务可用率:buckets 为窗口内可用率柱,services 为各服务百分比
	mux.HandleFunc("GET /api/services/availability", func(w http.ResponseWriter, r *http.Request) {
		hours, _ := strconv.Atoi(r.URL.Query().Get("hours"))
		if hours <= 0 || hours > 168 {
			hours = 12
		}
		const nBuckets = 48
		now := time.Now().UnixMilli()
		start := now - int64(hours)*3600*1000
		span := float64(now - start)
		okB := make([]int, nBuckets)
		totB := make([]int, nBuckets)
		maintB := make([]int, nBuckets)
		type agg struct {
			ok, tot, latSum, latN int
		}
		bySvc := map[string]*agg{}
		order := []string{}
		for _, p := range readHistoryAsc(cfg.dataDir, 0) {
			if p.Ts < start || len(p.Services) == 0 {
				continue
			}
			b := int(float64(p.Ts-start) / span * nBuckets)
			if b >= nBuckets {
				b = nBuckets - 1
			}
			if p.Maint {
				maintB[b]++
				continue // 维护点不计入可用率
			}
			for _, s := range p.Services {
				a, seen := bySvc[s.Key]
				if !seen {
					a = &agg{}
					bySvc[s.Key] = a
					order = append(order, s.Key)
				}
				a.tot++
				totB[b]++
				if s.State == "ok" {
					a.ok++
					okB[b]++
				}
				if s.Latency != nil {
					a.latSum += *s.Latency
					a.latN++
				}
			}
		}
		buckets := make([]map[string]any, nBuckets)
		for i := 0; i < nBuckets; i++ {
			ratio := 1.0
			if totB[i] > 0 {
				ratio = float64(okB[i]) / float64(totB[i])
			}
			// 维护柱:过半为维护点即整柱置灰,前端画满格灰;空桶标 empty,前端留白不断造 100%
			maint := maintB[i] > 0 && maintB[i]*2 >= maintB[i]+totB[i]
			buckets[i] = map[string]any{"ts": start + int64(float64(i)*span/nBuckets), "ratio": ratio, "maint": maint, "empty": totB[i] == 0 && maintB[i] == 0}
		}
		svcs := []map[string]any{}
		overallOk, overallTot := 0, 0
		for _, k := range order {
			a := bySvc[k]
			avg := 0
			if a.latN > 0 {
				avg = a.latSum / a.latN
			}
			pct := 100.0
			if a.tot > 0 {
				pct = float64(a.ok) / float64(a.tot) * 100
			}
			overallOk += a.ok
			overallTot += a.tot
			svcs = append(svcs, map[string]any{
				"key": k, "name": serviceName(k),
				"availability_pct": fmt.Sprintf("%.1f", pct), "avg_latency_ms": avg, "checks": a.tot,
			})
		}
		overall := 0.0
		if overallTot > 0 {
			overall = float64(overallOk) / float64(overallTot) * 100
		}
		writeJSON(w, 200, map[string]any{"hours": hours, "buckets": buckets, "services": svcs, "overall_pct": fmt.Sprintf("%.2f", overall)})
	})

	// 各服务延迟序列:供图表概览画多线图,自动降采样
	mux.HandleFunc("GET /api/services/latency", func(w http.ResponseWriter, r *http.Request) {
		hours, _ := strconv.Atoi(r.URL.Query().Get("hours"))
		if hours <= 0 || hours > 168 {
			hours = 24
		}
		maxPts, _ := strconv.Atoi(r.URL.Query().Get("maxpoints"))
		if maxPts <= 0 || maxPts > 1000 {
			maxPts = 240
		}
		cutoff := time.Now().Add(-time.Duration(hours) * time.Hour).UnixMilli()
		var pts []historyPoint
		for _, p := range readHistoryAsc(cfg.dataDir, 0) {
			if p.Ts >= cutoff && len(p.Services) > 0 {
				pts = append(pts, p)
			}
		}
		step := 1
		if len(pts) > maxPts {
			step = (len(pts) + maxPts - 1) / maxPts
		}
		series := map[string][][2]int64{}
		order := []string{}
		for i := 0; i < len(pts); i += step {
			for _, s := range pts[i].Services {
				if s.Latency == nil {
					continue
				}
				if _, ok := series[s.Key]; !ok {
					order = append(order, s.Key)
				}
				series[s.Key] = append(series[s.Key], [2]int64{pts[i].Ts, int64(*s.Latency)})
			}
		}
		out := []map[string]any{}
		for _, k := range order {
			out = append(out, map[string]any{"key": k, "name": serviceName(k), "series": series[k]})
		}
		writeJSON(w, 200, map[string]any{"hours": hours, "services": out, "maint_spans": maintSpans(pts)})
	})

	// 聚合看板:一次请求拿到状态 + 历史摘要 + 延迟分位 + 进行中的事件
	mux.HandleFunc("GET /api/overview", func(w http.ResponseWriter, r *http.Request) {
		var m map[string]any
		if e, ok := c.get("status"); ok {
			json.Unmarshal(e.body, &m)
		} else {
			body, status, err := fetchUpstream(r.Context(), cfg.upstreamBase+"/api/bot")
			if err != nil {
				writeJSON(w, http.StatusBadGateway, map[string]any{"error": "upstream unreachable"})
				return
			}
			if status == 200 {
				c.set("status", &cacheEntry{body: body, status: 200, fetchedAt: time.Now(), ttl: 20 * time.Second})
			}
			json.Unmarshal(body, &m)
		}
		if m == nil {
			m = map[string]any{}
		}
		m["history_summary"] = historySummary(cfg.dataDir)
		m["latency_stats"] = latencyStats(cfg.dataDir, 24)
		ongoing := []map[string]any{}
		for _, in := range incidents(cfg.dataDir) {
			if on, _ := in["ongoing"].(bool); on {
				ongoing = append(ongoing, in)
			}
		}
		m["ongoing_incidents"] = ongoing
		writeJSON(w, 200, m)
	})

	// SSE 实时推送:每次采集完成即广播,心跳 20s
	mux.HandleFunc("GET /api/stream", func(w http.ResponseWriter, r *http.Request) {
		flusher, ok := w.(http.Flusher)
		if !ok {
			writeJSON(w, 500, map[string]any{"error": "streaming unsupported"})
			return
		}
		w.Header().Set("Content-Type", "text/event-stream")
		w.Header().Set("Cache-Control", "no-cache")
		w.Header().Set("Connection", "keep-alive")
		w.Header().Set("X-Data-Source", attribution)
		send := func(b []byte) {
			fmt.Fprintf(w, "event: status\ndata: %s\n\n", bytes.TrimSpace(b))
			flusher.Flush()
		}
		if e, ok := c.get("status"); ok {
			send(e.body)
		}
		ch := streamHub.add()
		defer streamHub.remove(ch)
		heartbeat := time.NewTicker(20 * time.Second)
		defer heartbeat.Stop()
		for {
			select {
			case <-r.Context().Done():
				return
			case b := <-ch:
				send(b)
			case <-heartbeat.C:
				fmt.Fprintf(w, ": ping\n\n")
				flusher.Flush()
			}
		}
	})

	// OpenAPI 规格(内嵌 openapi.json)
	mux.HandleFunc("GET /api/openapi.json", func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("Content-Type", "application/json; charset=utf-8")
		w.Write(openapiSpec)
	})

	// 落雪公开曲目列表(无需登录,1h 缓存;查分 OAuth 已下线)
	lxnsCfg := loadLxnsConfig()
	mux.HandleFunc("GET /api/lxns/songs", handleLxnsSongs(lxnsCfg))

	// 上报转发:先验本地 8 位验证码(一次有效),再透传签名头到上游 bot-post,Body 限 1MB
	mux.HandleFunc("GET /api/report/captcha", func(w http.ResponseWriter, r *http.Request) {
		id, code := newCaptcha()
		writeJSON(w, 200, map[string]any{"captcha_id": id, "code": code, "expires_in": 300})
	})
	mux.HandleFunc("POST /api/report", func(w http.ResponseWriter, r *http.Request) {
		clientID := r.Header.Get("X-Client-ID")
		ts := r.Header.Get("X-Timestamp")
		sig := r.Header.Get("X-Signature")
		if clientID == "" || ts == "" || sig == "" {
			writeJSON(w, 400, map[string]any{"error": "missing X-Client-ID / X-Timestamp / X-Signature"})
			return
		}
		capID := r.Header.Get("X-Captcha-ID")
		capCode := r.Header.Get("X-Captcha-Code")
		if capID == "" || capCode == "" {
			writeJSON(w, 400, map[string]any{"error": "missing X-Captcha-ID / X-Captcha-Code"})
			return
		}
		if !verifyCaptcha(capID, capCode) {
			writeJSON(w, 403, map[string]any{"error": "invalid or expired captcha"})
			return
		}
		body, err := io.ReadAll(io.LimitReader(r.Body, 1<<20))
		if err != nil {
			writeJSON(w, 400, map[string]any{"error": "read body failed"})
			return
		}
		var arr []map[string]any
		if err := json.Unmarshal(body, &arr); err != nil {
			writeJSON(w, 400, map[string]any{"error": "body must be a JSON array"})
			return
		}
		req, err := http.NewRequestWithContext(r.Context(), http.MethodPost, cfg.reportUpstream+"/bot-post", bytes.NewReader(body))
		if err != nil {
			writeJSON(w, 500, map[string]any{"error": err.Error()})
			return
		}
		req.Header.Set("Content-Type", "application/json")
		req.Header.Set("X-Client-ID", clientID)
		req.Header.Set("X-Timestamp", ts)
		req.Header.Set("X-Signature", sig)
		resp, err := httpClient.Do(req)
		if err != nil {
			writeJSON(w, 502, map[string]any{"error": "report upstream unreachable", "detail": err.Error()})
			return
		}
		defer resp.Body.Close()
		rb, _ := io.ReadAll(io.LimitReader(resp.Body, 1<<20))
		w.Header().Set("Content-Type", "application/json; charset=utf-8")
		w.WriteHeader(resp.StatusCode)
		w.Write(rb)
	})

	handler := withCORS(cfg.corsAllow, mux)
	srv := &http.Server{Addr: ":" + cfg.port, Handler: handler, ReadHeaderTimeout: 10 * time.Second}

	// 后台采集:每 POLL_INTERVAL 抓一次状态并落盘
	ctx, stop := signal.NotifyContext(context.Background(), os.Interrupt, syscall.SIGTERM)
	defer stop()
	go func() {
		t := time.NewTicker(cfg.pollInterval)
		defer t.Stop()
		collect := func() {
			cctx, cancel := context.WithTimeout(context.Background(), 15*time.Second)
			defer cancel()
			body, status, err := fetchUpstream(cctx, cfg.upstreamBase+"/api/bot")
			if err != nil {
				log.Printf("poll: %v", err)
				return
			}
			if status != 200 {
				log.Printf("poll: upstream %d", status)
				return
			}
			c.set("status", &cacheEntry{body: body, status: 200, fetchedAt: time.Now(), ttl: 20 * time.Second})
			appendHistory(cfg.dataDir, body, cfg.historyMax)
			streamHub.broadcast(body)
		}
		collect()
		for {
			select {
			case <-t.C:
				collect()
			case <-ctx.Done():
				return
			}
		}
	}()

	go func() {
		log.Printf("gateway listening on :%s  upstream=%s poll=%s", cfg.port, cfg.upstreamBase, cfg.pollInterval)
		if err := srv.ListenAndServe(); err != nil && err != http.ErrServerClosed {
			log.Fatalf("listen: %v", err)
		}
	}()
	<-ctx.Done()
	shutdown, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel()
	srv.Shutdown(shutdown)
	log.Println("bye")
}

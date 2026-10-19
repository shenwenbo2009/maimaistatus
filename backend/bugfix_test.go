package main

// bugfix_audit 缺陷修复单测:覆盖 D-02/04/07/08/09/10/12/13/15/16/17,
// 沿用存量 maint_test.go 的表驱动 + t.TempDir 构造 JSONL 风格,零新测试框架依赖。

import (
	"bytes"
	"encoding/json"
	"fmt"
	"io"
	"net/http"
	"net/http/httptest"
	"os"
	"strings"
	"testing"
	"time"
)

// skipIfMaint 构造的相对时间点若撞上每日维护窗口(北京时间 04:00-07:00)则跳过,避免 flaky
func skipIfMaint(t *testing.T, now int64, hours int) {
	t.Helper()
	for i := 0; i <= hours; i++ {
		if inRoutineMaint(now - int64(i)*3600*1000) {
			t.Skip("test points overlap maintenance window")
		}
	}
}

// writePoints 把点位按行写入临时目录的 history.jsonl
func writePoints(t *testing.T, dir string, pts []historyPoint) {
	t.Helper()
	var b strings.Builder
	for _, p := range pts {
		line, err := json.Marshal(p)
		if err != nil {
			t.Fatal(err)
		}
		b.Write(line)
		b.WriteByte('\n')
	}
	if err := os.WriteFile(dir+"/history.jsonl", []byte(b.String()), 0o644); err != nil {
		t.Fatal(err)
	}
}

/* ---- D-02:代理缓存规则 ---- */

func TestIsCacheableStatus(t *testing.T) {
	cases := []struct {
		status int
		want   bool
	}{
		{200, true}, {201, true}, {299, true},
		{199, false}, {300, false}, {404, false}, {502, false},
	}
	for _, c := range cases {
		if got := isCacheableStatus(c.status); got != c.want {
			t.Errorf("isCacheableStatus(%d)=%v want %v", c.status, got, c.want)
		}
	}
}

func TestProxyCachedNoCacheOnError(t *testing.T) {
	count := 0
	up := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		count++
		if count == 1 {
			w.WriteHeader(http.StatusBadGateway)
			w.Write([]byte(`{"error":"boom"}`))
			return
		}
		w.Write([]byte(`{"ok":true}`))
	}))
	defer up.Close()
	c := newCache()
	h := proxyCached(c, "top100", up.URL, time.Hour)
	do := func() *httptest.ResponseRecorder {
		w := httptest.NewRecorder()
		h.ServeHTTP(w, httptest.NewRequest(http.MethodGet, "/api/top100", nil))
		return w
	}
	w1 := do()
	if w1.Code != http.StatusBadGateway || w1.Header().Get("X-Cache") != "MISS" {
		t.Fatalf("first: code=%d x-cache=%q want 502/MISS", w1.Code, w1.Header().Get("X-Cache"))
	}
	w2 := do()
	if w2.Code != http.StatusOK || w2.Header().Get("X-Cache") != "MISS" {
		t.Fatalf("second: code=%d x-cache=%q want 200/MISS (5xx must not be cached)", w2.Code, w2.Header().Get("X-Cache"))
	}
	if string(w2.Body.Bytes()) != `{"ok":true}` {
		t.Fatalf("second body=%q want fresh upstream body", w2.Body.String())
	}
}

/* ---- D-04:history/range 先过滤后截断 ---- */

func TestHistoryRangeFilterFirst(t *testing.T) {
	dir := t.TempDir()
	now := time.Now().UnixMilli()
	skipIfMaint(t, now, 24)
	var pts []historyPoint
	for i := 24; i >= 0; i-- { // 25 个点,每小时 1 个
		pts = append(pts, historyPoint{Ts: now - int64(i)*3600*1000, Verdict: "normal"})
	}
	writePoints(t, dir, pts)
	// 总点数(25)超过 limit(5),from/to 指向 20 小时前时段:修复前(先截最新 5 条再过滤)必为空
	from := now - 20*3600*1000 - 1800*1000
	to := now - 20*3600*1000 + 1800*1000
	got := historyRangePoints(dir, from, to, 5)
	if len(got) == 0 {
		t.Fatal("older window query returned empty (filter must run before truncation)")
	}
	for _, p := range got {
		if p.Ts < from || p.Ts > to {
			t.Fatalf("point ts=%d outside window [%d,%d]", p.Ts, from, to)
		}
	}
	for i := 1; i < len(got); i++ {
		if got[i-1].Ts < got[i].Ts {
			t.Fatal("points must be newest-first (desc)")
		}
	}
}

func TestHistoryRangeLimitAfterFilter(t *testing.T) {
	dir := t.TempDir()
	now := time.Now().UnixMilli()
	skipIfMaint(t, now, 24)
	var pts []historyPoint
	for i := 24; i >= 0; i-- {
		pts = append(pts, historyPoint{Ts: now - int64(i)*3600*1000, Verdict: "normal"})
	}
	writePoints(t, dir, pts)
	got := historyRangePoints(dir, 0, 0, 10)
	if len(got) != 10 {
		t.Fatalf("got %d points want 10 (latest limit after filter)", len(got))
	}
	if got[0].Ts != now || got[9].Ts != now-9*3600*1000 {
		t.Fatalf("limit window wrong: first=%d last=%d", got[0].Ts, got[9].Ts)
	}
}

/* ---- D-07/D-08/D-17:summary 空态、窗口口径、同源 ---- */

func TestSummaryEmptyState(t *testing.T) {
	s := historySummary(t.TempDir()) // 空目录,无历史文件
	if s["total"] != 0 {
		t.Fatalf("total=%v want 0", s["total"])
	}
	if s["uptime_pct"] != nil {
		t.Fatalf("uptime_pct=%v want nil (must not mislead as 0.00%% all-down)", s["uptime_pct"])
	}
}

func TestSummaryWindowAlignment(t *testing.T) {
	dir := t.TempDir()
	now := time.Now().UnixMilli()
	skipIfMaint(t, now, 13)
	lat := func(v int) *int { return &v }
	writePoints(t, dir, []historyPoint{
		{Ts: now - 13*3600*1000, Verdict: "normal", LatencyMs: lat(100)}, // 12h 窗口外
		{Ts: now - 11*3600*1000, Verdict: "normal", LatencyMs: lat(200)},
		{Ts: now - 3600*1000, Verdict: "normal", LatencyMs: lat(300)},
	})
	s := historySummary(dir)
	if s["total"] != 2 {
		t.Fatalf("total=%v want 2 (12h time window, independent of poll interval)", s["total"])
	}
	if s["avg_latency_ms"] != 250 {
		t.Fatalf("avg=%v want 250", s["avg_latency_ms"])
	}
}

func TestUptimeOverallSameSource(t *testing.T) {
	dir := t.TempDir()
	now := time.Now().UnixMilli()
	skipIfMaint(t, now, 2)
	ok := historySvc{Key: "net", State: "ok"}
	down := historySvc{Key: "net", State: "down"}
	writePoints(t, dir, []historyPoint{
		{Ts: now - 2*3600*1000, Verdict: "normal", Services: []historySvc{ok, ok}},
		{Ts: now - 3600*1000, Verdict: "outage", Services: []historySvc{ok, down}},
	})
	s := historySummary(dir)
	pct, tot, _ := overallAvailability(dir, 12)
	if tot != 4 {
		t.Fatalf("tot=%d want 4", tot)
	}
	if s["uptime_pct"] != fmt.Sprintf("%.2f", pct) {
		t.Fatalf("uptime=%v overall=%.2f must be same source (string equal)", s["uptime_pct"], pct)
	}
}

/* ---- D-12:延迟序列降采样保尾 ---- */

func TestLatencyDownsampleKeepsLast(t *testing.T) {
	// 点数不超 maxPts:全量
	idx := downsampleIndices(10, 240)
	if len(idx) != 10 || idx[len(idx)-1] != 9 {
		t.Fatalf("small set: idx=%v want full 0..9", idx)
	}
	// 点数超 maxPts:末尾强制追加最新点
	idx = downsampleIndices(100, 10)
	if idx[len(idx)-1] != 99 {
		t.Fatalf("last idx=%d want 99 (downsample must keep latest point)", idx[len(idx)-1])
	}
	if len(idx) > 11 {
		t.Fatalf("too many samples: %d (maxPts=10 +1 tail)", len(idx))
	}
	// 空集不越界
	if idx := downsampleIndices(0, 10); len(idx) != 0 {
		t.Fatalf("empty set: %v", idx)
	}
}

/* ---- D-13:事件时长不多算一个采集间隔 ---- */

func TestIncidentsDuration(t *testing.T) {
	dir := t.TempDir()
	base := time.Date(2026, 10, 11, 10, 0, 0, 0, time.UTC).UnixMilli() // 18:00 北京,避开维护窗口
	if inRoutineMaint(base) || inRoutineMaint(base+60000) {
		t.Skip("test points overlap maintenance window")
	}
	writePoints(t, dir, []historyPoint{
		{Ts: base - 60000, Verdict: "normal"},
		{Ts: base, Verdict: "outage"},       // 10:00:00 异常
		{Ts: base + 30000, Verdict: "outage"}, // 10:00:30 异常
		{Ts: base + 60000, Verdict: "normal"}, // 10:01:00 恢复
	})
	list := incidents(dir)
	if len(list) != 1 {
		t.Fatalf("got %d incidents want 1", len(list))
	}
	if list[0]["duration_sec"] != int64(30) {
		t.Fatalf("duration=%v want 30 (last abnormal - first abnormal, no extra interval)", list[0]["duration_sec"])
	}
	if list[0]["end_ts"] != base+30000 {
		t.Fatalf("end_ts=%v want last abnormal point ts", list[0]["end_ts"])
	}
}

/* ---- D-09/D-10/D-15/D-16:上报链路 ---- */

func TestCaptchaFailLimit(t *testing.T) {
	id, code, err := newCaptcha()
	if err != nil {
		t.Fatal(err)
	}
	wrong := "00000000"
	if wrong == code {
		wrong = "11111111"
	}
	for i := 0; i < captchaMaxFails; i++ {
		if verifyCaptcha(id, wrong) {
			t.Fatalf("wrong code accepted at attempt %d", i)
		}
	}
	if verifyCaptcha(id, code) {
		t.Fatal("captcha must be invalidated after 10 failed attempts (brute-force limit)")
	}
}

func TestCaptchaDigitsUniform(t *testing.T) {
	const n = 200 // 200 × 8 = 1600 样本
	counts := make([]int, 10)
	for i := 0; i < n; i++ {
		code, err := captchaDigits(8)
		if err != nil {
			t.Fatal(err)
		}
		for _, c := range []byte(code) {
			counts[c-'0']++
		}
	}
	expect := n * 8 / 10
	tol := expect / 5 // ±20% 宽松统计断言
	for d, c := range counts {
		if c < expect-tol || c > expect+tol {
			t.Fatalf("digit %d: count=%d outside [%d,%d] (rejection sampling broken, modulo bias?)", d, c, expect-tol, expect+tol)
		}
	}
}

func TestReportBodyTooLarge(t *testing.T) {
	big := bytes.Repeat([]byte("a"), 2<<20) // 2MB
	req := httptest.NewRequest(http.MethodPost, "/api/report", bytes.NewReader(big))
	req.Header.Set("X-Client-ID", "test")
	req.Header.Set("X-Timestamp", "0")
	req.Header.Set("X-Signature", "sig")
	// 用不存在的验证码:若 413 分支在验证码之前,则直接 413(同时证明验证码未被消耗)
	req.Header.Set("X-Captcha-ID", "nonexistent")
	req.Header.Set("X-Captcha-Code", "00000000")
	w := httptest.NewRecorder()
	handleReport("http://127.0.0.1:1", corptchaConfig{}, w, req) // 上游不可达,零值 config 走本地通道,但 413 应提前返回
	if w.Code != http.StatusRequestEntityTooLarge {
		t.Fatalf("code=%d want 413 (not silent-truncated misleading 400)", w.Code)
	}
	if !strings.Contains(w.Body.String(), "too large") {
		t.Fatalf("body=%q want 'too large' message", w.Body.String())
	}
}

type failReader struct{}

func (failReader) Read([]byte) (int, error) { return 0, io.ErrUnexpectedEOF }

func TestCaptchaRandFailure(t *testing.T) {
	old := randReader
	randReader = failReader{}
	defer func() { randReader = old }()
	if _, _, err := newCaptcha(); err == nil {
		t.Fatal("newCaptcha must fail when rand source fails (never issue weak deterministic code)")
	}
}
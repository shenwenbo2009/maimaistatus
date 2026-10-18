package main

import (
	"encoding/json"
	"os"
	"testing"
	"time"
)

// 维护窗口默认 04:00-07:00 GMT+8(中国无夏令时,固定 UTC+8)

// 北京时间某日某时 → 毫秒时间戳
func beijingMs(day time.Time, h, m int) int64 {
	return time.Date(day.Year(), day.Month(), day.Day(), h, m, 0, 0, time.FixedZone("CST", 8*3600)).UnixMilli()
}

func TestInRoutineMaint(t *testing.T) {
	day := time.Date(2026, 10, 11, 0, 0, 0, 0, time.UTC)
	cases := []struct {
		h, m int
		want bool
	}{
		{3, 59, false},   // 窗口前
		{4, 0, true},     // 左闭
		{5, 30, true},    // 窗口内
		{6, 59, true},    // 右开前
		{7, 0, false},    // 右开
		{12, 0, false},   // 白天
		{23, 59, false},  // 深夜
	}
	for _, c := range cases {
		if got := inRoutineMaint(beijingMs(day, c.h, c.m)); got != c.want {
			t.Errorf("%02d:%02d got %v want %v", c.h, c.m, got, c.want)
		}
	}
}

func TestIsMaintPoint(t *testing.T) {
	day := time.Date(2026, 10, 11, 0, 0, 0, 0, time.UTC)
	// 上游报 maintenance:任何时段都算维护
	if !isMaintPoint(historyPoint{Ts: beijingMs(day, 12, 0), Verdict: "maintenance"}) {
		t.Error("verdict maintenance should be maint")
	}
	// 窗口内普通点位也算维护
	if !isMaintPoint(historyPoint{Ts: beijingMs(day, 5, 0), Verdict: "normal"}) {
		t.Error("in-window normal point should be maint")
	}
	// 窗口外正常点位不算
	if isMaintPoint(historyPoint{Ts: beijingMs(day, 12, 0), Verdict: "normal"}) {
		t.Error("out-of-window normal point should not be maint")
	}
	// 窗口外宕机不算维护(保持原判定)
	if isMaintPoint(historyPoint{Ts: beijingMs(day, 12, 0), Verdict: "outage"}) {
		t.Error("out-of-window outage should not be maint")
	}
}

func TestMaintSpans(t *testing.T) {
	day := time.Date(2026, 10, 11, 0, 0, 0, 0, time.UTC)
	mk := func(h, m int) historyPoint {
		return historyPoint{Ts: beijingMs(day, h, m), Verdict: "normal"}
	}
	pts := []historyPoint{mk(3, 50), mk(4, 5), mk(4, 10), mk(4, 15), mk(8, 0)}
	spans := maintSpans(pts)
	if len(spans) != 1 {
		t.Fatalf("got %d spans want 1: %v", len(spans), spans)
	}
	if spans[0][0] != beijingMs(day, 4, 5) || spans[0][1] != beijingMs(day, 4, 15) {
		t.Fatalf("span wrong: %v", spans[0])
	}
	// 间隔超 10 分钟即断开
	pts2 := []historyPoint{mk(4, 5), mk(4, 40)}
	if s2 := maintSpans(pts2); len(s2) != 2 {
		t.Fatalf("got %d spans want 2: %v", len(s2), s2)
	}
}

func TestOverallAvailability(t *testing.T) {
	dir := t.TempDir()
	now := time.Now().UnixMilli()
	mkLine := func(ts int64, verdict string, svcs []historySvc) string {
		b, _ := json.Marshal(historyPoint{Ts: ts, Verdict: verdict, Services: svcs})
		return string(b) + "\n"
	}
	ok := historySvc{Key: "net", State: "ok"}
	down := historySvc{Key: "net", State: "down"}
	lines := mkLine(now-3600*1000, "normal", []historySvc{ok, ok}) +
		mkLine(now-1800*1000, "outage", []historySvc{ok, down}) +
		mkLine(now-900*1000, "maintenance", []historySvc{down, down})
	if err := os.MkdirAll(dir, 0o755); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(dir+"/history.jsonl", []byte(lines), 0o644); err != nil {
		t.Fatal(err)
	}
	pct, tot, maint := overallAvailability(dir, 12)
	if tot != 4 || maint != 1 {
		t.Fatalf("tot=%d maint=%d want 4/1", tot, maint)
	}
	if pct < 74.99 || pct > 75.01 { // 3/4
		t.Fatalf("pct=%v want 75.00", pct)
	}
	s := historySummary(dir)
	if s["uptime_pct"] != "75.00" {
		t.Fatalf("summary uptime=%v want 75.00(与 availability 同源)", s["uptime_pct"])
	}
}

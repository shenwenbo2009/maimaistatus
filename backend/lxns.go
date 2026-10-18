// 落雪公开曲目列表代理(无需登录,1h 缓存)
// 查分相关 OAuth 已下线,仅保留曲目信息(供难度校准等公开用途)。
package main

import (
	"context"
	"fmt"
	"net/http"
	"sync"
	"time"
)

type lxnsConfig struct {
	base string
}

func loadLxnsConfig() lxnsConfig {
	return lxnsConfig{
		base: getenv("LXNS_BASE", "https://maimai.lxns.net"),
	}
}

var songsCacheMu sync.Mutex
var songsCacheBody []byte
var songsCacheAt time.Time

func fetchLxnsSongList(cfg lxnsConfig) ([]byte, error) {
	songsCacheMu.Lock()
	if time.Since(songsCacheAt) < time.Hour && songsCacheBody != nil {
		defer songsCacheMu.Unlock()
		return songsCacheBody, nil
	}
	songsCacheMu.Unlock()
	body, status, err := fetchUpstream(context.Background(), cfg.base+"/api/v0/maimai/song/list")
	if err != nil || status != 200 {
		return nil, fmt.Errorf("song list unreachable")
	}
	songsCacheMu.Lock()
	songsCacheBody, songsCacheAt = body, time.Now()
	songsCacheMu.Unlock()
	return body, nil
}

func handleLxnsSongs(cfg lxnsConfig) http.HandlerFunc {
	return func(w http.ResponseWriter, r *http.Request) {
		rb, err := fetchLxnsSongList(cfg)
		if err != nil {
			writeJSON(w, 502, map[string]any{"error": "lxns song list unreachable"})
			return
		}
		w.Header().Set("Content-Type", "application/json; charset=utf-8")
		w.Write(rb)
	}
}

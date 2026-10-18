#!/bin/sh
# 容器内双进程启动:网关(后台)+前端(前台),信号透传,任一退出则全停
set -e

GATEWAY_PORT="${GATEWAY_PORT:-9560}"
FRONTEND_PORT="${FRONTEND_PORT:-9561}"

cd /app/backend
PORT="$GATEWAY_PORT" ./gateway &
GATEWAY_PID=$!

cd /app/frontend
PORT="$FRONTEND_PORT" node .output/server/index.mjs &
FRONTEND_PID=$!

term() {
  kill "$GATEWAY_PID" "$FRONTEND_PID" 2>/dev/null
}
trap term TERM INT

wait -n "$GATEWAY_PID" "$FRONTEND_PID"
EXIT_CODE=$?
term
wait 2>/dev/null
exit "$EXIT_CODE"

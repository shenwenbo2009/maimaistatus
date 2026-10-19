#!/bin/sh
# 容器内双进程启动:网关(后台)+前端(前台),信号透传,任一退出则全停。
# 注意:不用 set -e —— wait -n 捕获子进程退出码属正常路径,
# set -e 会在子进程非零退出时跳过清理函数,产生孤儿进程;所有退出路径显式控制。

GATEWAY_PORT="${GATEWAY_PORT:-9560}"
FRONTEND_PORT="${FRONTEND_PORT:-9561}"

cd /app/backend || exit 1
PORT="$GATEWAY_PORT" ./gateway &
GATEWAY_PID=$!

cd /app/frontend || exit 1
PORT="$FRONTEND_PORT" node .output/server/index.mjs &
FRONTEND_PID=$!

EXIT_CODE=0

term() {
  kill "$GATEWAY_PID" "$FRONTEND_PID" 2>/dev/null
}

trap term TERM INT

# 任一子进程退出即收尾:捕获其退出码,终止另一个,收尸后传播退出码
wait -n "$GATEWAY_PID" "$FRONTEND_PID" || EXIT_CODE=$?
term
wait 2>/dev/null
exit "$EXIT_CODE"

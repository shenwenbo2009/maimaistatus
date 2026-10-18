# 容器编排(All in One)

单 Alpine 容器同时跑 Go 网关(`:9560`)+ Nuxt 前端(`:9561`)，apk / npm / Go Proxy 全切国内源。

## 启动

```bash
cd container
cp .env.example .env   # 按需改地址、密钥、端口
docker compose up -d --build
curl http://127.0.0.1:9560/healthz
```

- 前端 `http://宿主:9561`，网关 API `http://宿主:9560`。
- 历史落盘持久化在 `gateway-data` 卷(`/app/backend/data`)，删容器不丢数：`docker volume inspect wahlap-status_gateway-data`。
- 日志：`docker compose logs -f`；更新代码后：`docker compose up -d --build`。

## 注意

- `NUXT_PUBLIC_*` 烘焙进前端构建产物，改它们必须 `--build`；`POLL_INTERVAL_SEC` / `MAINT_WINDOWS` / `CORS_ALLOW` 运行时生效，`up -d` 即可。
- 浏览器必须能直连网关地址(`NUXT_PUBLIC_API_BASE`)，前后端分机部署时填前端可达的网关 URL。

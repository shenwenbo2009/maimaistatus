# 上线部署清单(maimai.imikufans.cn)

## 0. 准备

服务器需安装 Go ≥1.23、Node ≥20、nginx、certbot。域名已解析到本机。

## 1. 拉代码

```bash
git clone https://github.com/shenwenbo2009/maimaistatus.git
cd maimaistatus
```

## 2. 后端

```bash
cd backend
go build -o gateway .
# 环境变量(写入 /etc/maimai-gateway.env,权限 600):
#   PORT=9560
#   MAINT_WINDOWS=04:00-07:00
```

systemd(见 `deploy/maimai-gateway.service`,改 `User=` 与 `WorkingDirectory=` 后):

```bash
sudo cp deploy/maimai-gateway.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now maimai-gateway
curl http://127.0.0.1:9560/healthz
curl http://127.0.0.1:9560/api/lxns/songs | head -c 200  # 公开曲目列表应有返回
```

## 3. 前端

```bash
cd ../frontend
npm install
NUXT_PUBLIC_API_BASE=https://maimai.imikufans.cn/api npm run build
```

用 pm2 或 systemd 常驻 `.output/server/index.mjs`(端口 9561):

```bash
node .output/server/index.mjs  # Nuxt 默认 3000,本项目用 PORT=9561 node .output/server/index.mjs 启动
```

## 4. 反代与证书

nginx 见 `deploy/nginx.conf`(改域名与本机端口),要点:

- `/` → 127.0.0.1:9561(Nuxt SSR)
- `/api/` → 127.0.0.1:9560,注意 `/api/stream` 必须 `proxy_buffering off`(SSE)
- certbot 申请证书后重载 nginx

```bash
sudo cp deploy/nginx.conf /etc/nginx/sites-enabled/maimai
sudo nginx -t && sudo systemctl reload nginx
sudo certbot --nginx -d maimai.imikufans.cn
```

## 5. 验证

- `https://maimai.imikufans.cn/` 首页状态正常
- `https://maimai.imikufans.cn/api/healthz` → `{"ok":true,...}`

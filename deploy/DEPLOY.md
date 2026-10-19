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
#   CORPTCHA_SITE_KEY / CORPTCHA_SECRET(可选,两值齐备才启用 Corptcha 人机验证,
#   见下文「启用 Corptcha 人机验证」;Secret 严禁提交仓库)
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
## 6. 启用 Corptcha 人机验证(可选)

默认不配置任何新变量,上报验证走本地 8 位数字验证码,与基线行为完全一致。

1. 在 https://dash.corptcha.com 注册并在「站点管理」创建站点,获取 Site ID(形如
   `cpt_xxxxxxxxxxxx`)与 Secret;**控制台中该站点的 purpose 须配置为 `report`**,
   否则核验返回 TOKEN_PURPOSE_MISMATCH(上报 403)。
2. 注入 3 个环境变量:
   - 后端(运行时,写入 `/etc/maimai-gateway.env` 后 `sudo systemctl restart maimai-gateway`):
     `CORPTCHA_SITE_KEY` 与 `CORPTCHA_SECRET`(Secret 仅存在于后端进程环境,
     不会出现在日志、响应与前端产物中)
   - 前端(构建期烘焙进产物,改值需重新 build):
     `NUXT_PUBLIC_CORPTCHA_SITE_KEY=cpt_xxx npm run build`
3. **前后端 siteKey 必须同值**:不一致不会报配置错误,而是验证始终失败——
   上报 403 响应 detail 中的 errorCode(UNAUTHORIZED / TOKEN_NOT_FOUND 类)是排查入口。
4. 重启后访问 `https://<域名>/api/report/mode` 应返回 `{"mode":"corptcha","degraded":false,"purpose":"report"}`,
   打开上报弹窗完成一次人机验证并成功上报即接入完成。
5. Corptcha 服务故障时网关自动降级回本地数字验证码(30s 冷却后自动探测恢复),
   上报不中断;第三方 API 调用方的本地验证码通道不受任何影响。

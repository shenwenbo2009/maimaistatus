# 单容器(All in One):Alpine + Go 网关(:9560)+ Nuxt 前端(:9561)
FROM alpine:3.21

# ---- apk 换国内源(阿里云)并安装 Node.js + Go ----
RUN sed -i 's/dl-cdn.alpinelinux.org/mirrors.aliyun.com/g' /etc/apk/repositories && \
    apk add --no-cache nodejs npm go && \
    node --version && npm --version && go version

# ---- 国内镜像源:Go Proxy + npm registry ----
ENV GOPROXY=https://goproxy.cn,direct
RUN npm config set registry https://registry.npmmirror.com

WORKDIR /app

# ---- 后端:标准库零依赖,直接编译 ----
COPY backend/ ./backend/
RUN cd backend && go build -o /app/backend/gateway .

# ---- 前端:先装依赖再全量拷,构建完瘦身 ----
COPY frontend/package.json frontend/package-lock.json ./frontend/
RUN cd frontend && rm -rf node_modules .nuxt .output && npm ci
COPY frontend/ ./frontend/

# Nuxt PUBLIC 变量烘焙进构建产物,改值需重 build(见 README)
ARG NUXT_PUBLIC_API_BASE=http://localhost:9560
ARG NUXT_PUBLIC_REPORT_CLIENT_ID=<your-key>
ARG NUXT_PUBLIC_REPORT_PRIVATE_KEY=<your-key>
ENV NUXT_PUBLIC_API_BASE=$NUXT_PUBLIC_API_BASE \
    NUXT_PUBLIC_REPORT_CLIENT_ID=$NUXT_PUBLIC_REPORT_CLIENT_ID \
    NUXT_PUBLIC_REPORT_PRIVATE_KEY=$NUXT_PUBLIC_REPORT_PRIVATE_KEY
RUN cd frontend && npm run build && rm -rf node_modules .nuxt

COPY container/entrypoint.sh /entrypoint.sh
RUN chmod +x /entrypoint.sh

# 网关 9560 / 前端 9561(容器内固定,宿主机映射见 compose)
EXPOSE 9560 9561
VOLUME ["/app/backend/data"]

ENTRYPOINT ["/entrypoint.sh"]

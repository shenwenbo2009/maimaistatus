// https://nuxt.com/docs/api/configuration/nuxt-config
export default defineNuxtConfig({
  compatibilityDate: '2025-11-01',
  devtools: { enabled: false },
  modules: ['@nuxt/ui'],
  ui: {
    // 本站用系统字体栈(main.css 自带),不需要 Google Fonts,直接关掉 @nuxt/fonts
    fonts: false
  },
  devServer: {
    port: 9561,
    host: '0.0.0.0' // 局域网设备可通过宿主 IP 访问,仅监听 localhost 则公网/局域网进不来
  },
  css: ['~/assets/css/main.css'],
  app: {
    head: {
      title: '华立服务器死了吗？— 舞萌DX 状态监控',
      meta: [
        { charset: 'utf-8' },
        { name: 'viewport', content: 'width=device-width, initial-scale=1' },
        { name: 'description', content: '舞萌DX 服务器实时监测：探针状态、延迟、全服排行。数据来源于 isMaiDown by Chongxi。' }
      ]
    }
  },
  runtimeConfig: {
    public: {
      // Go 网关地址,生产环境通过 NUXT_PUBLIC_API_BASE 覆盖
      apiBase: process.env.NUXT_PUBLIC_API_BASE || 'http://localhost:9560',
      // 网关不可用时直连上游兜底
      upstreamBase: 'https://mai.chongxi.us',
      // 手动上报签名密钥(发邮件至 qwq@chongxi.us 获取后填入环境变量)
      reportClientId: process.env.NUXT_PUBLIC_REPORT_CLIENT_ID || '<your-key>',
      reportPrivateKey: process.env.NUXT_PUBLIC_REPORT_PRIVATE_KEY || '<your-key>'
    }
  },
  colorMode: {
    preference: 'system',
    fallback: 'dark',
    classSuffix: ''
  }
})

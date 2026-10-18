<template>
  <DesignSystem
    title="关于 / 接口文档"
    description="本站为非官方平台,数据不代表华立科技官方立场,与华立科技及世嘉官方无任何关联。"
  >
    <template #actions>
      <UButton
        to="/docs"
        color="primary"
        variant="solid"
        size="sm"
        icon="i-lucide-book-open"
        trailing-icon="i-lucide-arrow-right"
        class="cursor-pointer"
      >
        点击进入开发者文档
      </UButton>
    </template>

    <div class="grid grid-cols-1 md:grid-cols-2 gap-4">
      <UCard title="数据来源">
        <p class="text-sm">
          平台数据由机台采集、服务器探针、玩家上报、Bot 关键词监听四路实时聚合,源自
          <a href="https://mai.chongxi.us" target="_blank" rel="noopener" class="text-primary">isMaiDown by Chongxi</a>。
        </p>
        <p class="dim mt-2">本站数据免费公开,可自由使用 / 分发 / 商业使用,需标注「数据来源于 isMaiDown by Chongxi」。</p>
        <p class="dim mt-1">QQ 群:176358258 · 邮箱:qwq@chongxi.us</p>
      </UCard>
      <UCard title="本项目架构">
        <p class="text-sm"><code class="inline">backend/</code> Go 网关:代理 + 缓存上游接口,每 30 秒采集状态落盘为本地历史,并透传上报签名。</p>
        <p class="text-sm mt-2"><code class="inline">frontend/</code> Nuxt 前端:本页面即由 Nuxt 渲染,API 基址通过 <code class="inline">NUXT_PUBLIC_API_BASE</code> 配置。</p>
        <p class="dim mt-2">网关不可用时,前端会自动直连 <code class="inline">https://mai.chongxi.us</code> 兜底(历史页除外)。接口明细见开发者文档。</p>
      </UCard>
    </div>
  </DesignSystem>
</template>

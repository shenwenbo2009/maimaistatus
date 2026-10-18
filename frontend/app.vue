<template>
  <UApp>
    <header class="nav">
      <div class="nav-inner">
        <NuxtLink to="/" class="brand">
          <span class="brand-dot" :class="dotClass"></span>
          华立服务器死了吗?
        </NuxtLink>
        <UNavigationMenu
          :items="navItems"
          highlight
          color="primary"
          variant="pill"
          class="nav-menu"
        />
        <div class="nav-right">
          <ColorModeButton />
        </div>
      </div>
    </header>
    <NuxtPage />
    <footer class="footer">
      数据来源于 <a href="https://mai.chongxi.us" target="_blank" rel="noopener">isMaiDown by Chongxi</a>
      · 本站为非官方平台,与华立科技及世嘉官方无任何关联
    </footer>
  </UApp>
</template>

<script setup lang="ts">
import type { NavigationMenuItem } from '@nuxt/ui'

// 右侧导航栏(Nuxt UI 统一管理,新增页面只改这里)
const navItems: NavigationMenuItem[] = [
  { label: '监控', to: '/', icon: 'i-lucide-activity' },
  { label: '历史', to: '/history', icon: 'i-lucide-history' },
  { label: '全服乐曲', to: '/top100', icon: 'i-lucide-music' },
  { label: '全服排行', to: '/ratinglist', icon: 'i-lucide-trophy' },
  { label: '越级排行', to: '/chart145', icon: 'i-lucide-trending-up' },
  { label: '关于/文档', to: '/about', icon: 'i-lucide-book-open' }
]

// 顶栏圆点跟随实时状态变色
const { data } = await useFetch('/api/status', {
  baseURL: useRuntimeConfig().public.apiBase,
  server: false,
  lazy: true,
  retry: 0
})
const dotClass = computed(() => {
  const v = (data.value as any)?.verdict
  if (v === 'normal' || v === undefined) return ''
  if (v === 'outage') return 'down'
  return 'warn'
})
</script>

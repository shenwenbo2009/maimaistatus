<!-- 右上方主题切换:深色 / 浅色 / 跟随系统 -->
<script setup lang="ts">
import type { DropdownMenuItem } from '@nuxt/ui'

const colorMode = useColorMode()

const curIcon = computed(() => {
  if (colorMode.preference === 'dark') return 'i-lucide-moon'
  if (colorMode.preference === 'light') return 'i-lucide-sun'
  return 'i-lucide-monitor'
})

const curLabel = computed(() => {
  if (colorMode.preference === 'dark') return '深色'
  if (colorMode.preference === 'light') return '浅色'
  return '跟随系统'
})

const items = computed<DropdownMenuItem[]>(() => [
  {
    label: '跟随系统',
    icon: 'i-lucide-monitor',
    onSelect: () => { colorMode.preference = 'system' }
  },
  {
    label: '浅色',
    icon: 'i-lucide-sun',
    onSelect: () => { colorMode.preference = 'light' }
  },
  {
    label: '深色',
    icon: 'i-lucide-moon',
    onSelect: () => { colorMode.preference = 'dark' }
  }
])
</script>

<template>
  <ClientOnly v-if="!colorMode?.forced">
    <UDropdownMenu :items="items" :content="{ align: 'end', side: 'bottom' }">
      <UButton
        :icon="curIcon"
        color="neutral"
        variant="ghost"
        :aria-label="`主题:${curLabel},点击切换`"
      />
    </UDropdownMenu>
    <template #fallback>
      <div class="size-8" />
    </template>
  </ClientOnly>
</template>

<template>
  <DesignSystem
    title="全服 Rating 分布"
    description="每 100 分一段 · 从高到低"
    :loading="loading"
    :error="error"
    @retry="load"
  >
    <template #actions>
      <UBadge color="warning" variant="soft" size="lg">总玩家 {{ fmtNum(cur.total) }}</UBadge>
    </template>

    <UTabs
      v-model="curKey"
      :content="false"
      :items="tabItems"
      color="primary"
      variant="pill"
      size="sm"
      class="mb-4"
    />

    <UCard :title="`${cur.label} · 每 100 Rating 一段`">
      <div class="flex flex-col gap-2.5">
        <div
          v-for="b in sortedData"
          :key="b.bucket"
          class="grid grid-cols-[86px_1fr_76px] gap-3 items-center rounded-xl px-2 py-1.5 hover:bg-elevated/60 transition-colors"
        >
          <span class="dim tabular-nums">{{ b.bucket }}–{{ b.bucket + 99 }}</span>
          <div class="flex items-center gap-2">
            <UProgress
              :model-value="b.count"
              :max="maxCount"
              :color="b.count === maxCount ? 'success' : 'primary'"
              size="md"
              class="flex-1"
            />
            <UBadge v-if="b.count === maxCount" color="success" variant="soft" size="sm">峰值</UBadge>
          </div>
          <span class="text-right tabular-nums font-bold">{{ fmtNum(b.count) }}</span>
        </div>
      </div>
    </UCard>
  </DesignSystem>
</template>

<script setup lang="ts">
import type { TabsItem } from '@nuxt/ui'
import { apiGet, fmtNum } from '~/composables/useApi'

interface Bucket { bucket: number; count: number }
interface Ver { key: string; label: string; total: number; data: Bucket[] }
const versions = ref<Ver[]>([])
const curKey = ref('')
const loading = ref(true)
const error = ref('')

const cur = computed<Ver>(() => versions.value.find(v => v.key === curKey.value) || { key: '', label: '', total: 0, data: [] })
const maxCount = computed(() => Math.max(1, ...cur.value.data.map(b => b.count)))
// 从高到低展示
const sortedData = computed(() => [...cur.value.data].sort((a, b) => b.bucket - a.bucket))
// 版本 Tabs:默认 DX2026,徽章带各版本人数
const tabItems = computed<TabsItem[]>(() =>
  versions.value.map(v => ({ label: v.label, value: v.key, badge: fmtNum(v.total) }))
)

async function load() {
  loading.value = true
  error.value = ''
  try {
    const d = await apiGet<{ versions: any[]; data: Ver[] }>('/api/ratinglist')
    versions.value = d.data
    // 默认 DX2026,兜底末版
    curKey.value = d.data.find(v => v.key === 'dx2026')?.key || d.data[d.data.length - 1]?.key || ''
  } catch (e: any) {
    error.value = e?.message || '网络错误'
  } finally {
    loading.value = false
  }
}
onMounted(load)
</script>

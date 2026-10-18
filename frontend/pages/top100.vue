<template>
  <DesignSystem
    title="全服乐曲 Top100"
    description="按全服游玩次数排序"
    :loading="loading"
    :error="error"
    @retry="load"
  >
    <template #actions>
      <UBadge color="warning" variant="soft" size="lg">总游玩 {{ fmtNum(total) }}</UBadge>
    </template>

    <UInput
      v-model="q"
      icon="i-lucide-search"
      placeholder="搜索曲名或 ID…"
      class="w-full mb-4"
    />

    <UCard>
      <UTable :data="paged" :columns="columns" class="w-full" />
      <div v-if="!filtered.length" class="loading">没有匹配的乐曲</div>
      <div v-else class="mt-4 flex items-center justify-between gap-3 flex-wrap">
        <span class="dim">第 {{ (page - 1) * PAGE_SIZE + 1 }}–{{ Math.min(page * PAGE_SIZE, filtered.length) }} 首 / 共 {{ filtered.length }} 首</span>
        <UPagination
          v-model:page="page"
          :items-per-page="PAGE_SIZE"
          :total="filtered.length"
          show-edges
          :sibling-count="1"
        />
      </div>
    </UCard>
  </DesignSystem>
</template>

<script setup lang="ts">
import { h, resolveComponent } from 'vue'
import type { TableColumn } from '@nuxt/ui'
import { apiGet, coverUrl, fmtNum } from '~/composables/useApi'

const UAvatar = resolveComponent('UAvatar')

interface Song { id: number; name: string; plays: number; cover: string }
interface SongRow extends Song { rank: number }

const songs = ref<SongRow[]>([])
const total = ref(0)
const q = ref('')
const loading = ref(true)
const error = ref('')

// 分页:每页 20 首
const PAGE_SIZE = 20
const page = ref(1)
watch(q, () => { page.value = 1 })

const filtered = computed(() => {
  const k = q.value.trim().toLowerCase()
  if (!k) return songs.value
  return songs.value.filter(s => s.name.toLowerCase().includes(k) || String(s.id).includes(k))
})
const paged = computed(() => filtered.value.slice((page.value - 1) * PAGE_SIZE, page.value * PAGE_SIZE))
// ID 最高位:百位/千位(<10000)→STD;1 开头万位数(10000–19999)→DX
function isDX(id: number): boolean {
  return id >= 10000 && id < 20000
}
function pct(n: number) {
  return total.value ? ((n / total.value) * 100).toFixed(2) : '0.00'
}

const columns: TableColumn<SongRow>[] = [
  {
    accessorKey: 'rank',
    header: '#',
    cell: ({ row }) => {
      const r = row.getValue('rank') as number
      return h('div', { class: ['rank', r <= 3 ? `r${r}` : ''] }, r)
    }
  },
  {
    accessorKey: 'cover',
    header: '',
    cell: ({ row }) => {
      const o = row.original as SongRow
      return h(UAvatar, { src: coverUrl(o.cover), alt: o.name, size: 'lg', class: 'cover' })
    }
  },
  {
    accessorKey: 'name',
    header: '曲名',
    cell: ({ row }) => {
      const o = row.original as SongRow
      const dx = isDX(o.id)
      return h('div', { class: 't' }, [
        o.name,
        h('span', { class: ['badge', dx ? 'dx' : 'std'] }, dx ? 'DX' : 'STD')
      ])
    }
  },
  {
    accessorKey: 'id',
    header: 'ID',
    cell: ({ row }) => h('div', { class: 's' }, `ID ${row.getValue('id')}`)
  },
  {
    accessorKey: 'plays',
    header: '游玩人数',
    meta: { class: { th: 'text-right', td: 'text-right' } },
    cell: ({ row }) => h('div', { class: 'plays' }, fmtNum(row.getValue('plays') as number))
  },
  {
    id: 'pct',
    header: '占据的百分比',
    meta: { class: { th: 'text-right', td: 'text-right' } },
    cell: ({ row }) => h('div', { class: 's' }, `占 ${pct((row.original as SongRow).plays)}%`)
  }
]

async function load() {
  loading.value = true
  error.value = ''
  try {
    const d = await apiGet<{ totalPlays: number; songs: Song[] }>('/api/top100')
    total.value = d.totalPlays
    songs.value = d.songs.map((s, i) => ({ ...s, rank: i + 1 }))
  } catch (e: any) {
    error.value = e?.message || '网络错误'
  } finally {
    loading.value = false
  }
}
onMounted(load)
</script>

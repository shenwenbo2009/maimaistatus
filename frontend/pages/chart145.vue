<template>
  <DesignSystem
    title="越级排行 · 定数 14.5–15.0"
    description="按游玩人数排序 · 难度档已按落雪定数校准"
    :loading="loading"
    :error="error"
    @retry="load"
  >
    <template #actions>
      <UBadge color="warning" variant="soft" size="lg">合计 {{ fmtNum(total) }} 人次</UBadge>
    </template>

    <div class="flex gap-2 flex-wrap items-center mb-4">
      <UTabs
        v-model="tier"
        :content="false"
        :items="tierTabs"
        color="primary"
        variant="pill"
        size="sm"
      />
      <UInput
        v-model="q"
        icon="i-lucide-search"
        placeholder="搜索曲名…"
        class="flex-1 min-w-[200px]"
      />
    </div>

    <UCard>
      <UTable :data="paged" :columns="columns" class="w-full" />
      <div v-if="!filtered.length" class="loading">没有匹配的谱面</div>
      <div v-else-if="filtered.length > PAGE_SIZE" class="mt-4 flex items-center justify-between gap-3 flex-wrap">
        <span class="dim">第 {{ (page - 1) * PAGE_SIZE + 1 }}–{{ Math.min(page * PAGE_SIZE, filtered.length) }} 张 / 共 {{ filtered.length }} 张</span>
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
import type { TableColumn, TabsItem } from '@nuxt/ui'
import { apiGet, coverUrl, fmtNum } from '~/composables/useApi'

const UAvatar = resolveComponent('UAvatar')

// KOP 圈叫法:ADV(ADVANCED)/EXP(EXPERT)/MAS(MASTER)/ReM(Re:MASTER)
type Tier = 'adv' | 'exp' | 'mas' | 'rem'
const TIER_LABEL: Record<Tier, string> = { adv: 'ADV', exp: 'EXP', mas: 'MAS', rem: 'ReM' }

// ID 最高位:百位/千位(<10000)→STD;1 开头万位数(10000–19999)→DX(与全服乐曲页同规则,见落雪曲目 ID)
function isDX(id: number): boolean {
  return id >= 10000 && id < 20000
}
// 上游 level 整体低标了一档(3≈真 MAS,4≈真 ReM),已用落雪 level_value 逐首核验。
// 此表只收录明确核验过的例外;新曲默认走系统规则。
const TIER_FIX: Record<string, Tier> = {
  '834@15': 'rem',
  '799@14.9': 'rem',
  '11663@15': 'rem'
}
function resolveTier(musicId: number, upLevel: number, rate: number): Tier {
  const fix = TIER_FIX[`${musicId}@${rate}`]
  if (fix) return fix
  if (upLevel >= 4) return 'rem'
  if (upLevel === 3) return 'mas'
  if (upLevel === 2) return 'exp'
  return 'adv'
}

interface Chart { rank: number; players: number; musicId: number; name: string; level: number; rate: number; avg: number; med: number; mode: number; cover: string | null }
interface Row extends Chart { tier: Tier }

const songs = ref<Row[]>([])
const total = ref(0)
const q = ref('')
const tier = ref<'all' | Tier>('all')
const loading = ref(true)
const error = ref('')

const PAGE_SIZE = 20
const page = ref(1)
watch([q, tier], () => { page.value = 1 })

const tierTabs: TabsItem[] = [
  { label: '全部', value: 'all' },
  { label: 'ADV', value: 'adv' },
  { label: 'EXP', value: 'exp' },
  { label: 'MAS', value: 'mas' },
  { label: 'ReM', value: 'rem' }
]

const filtered = computed(() => songs.value.filter(s =>
  (tier.value === 'all' || s.tier === tier.value) &&
  (!q.value.trim() || s.name.toLowerCase().includes(q.value.trim().toLowerCase()))
))
const paged = computed(() => filtered.value.slice((page.value - 1) * PAGE_SIZE, page.value * PAGE_SIZE))

const columns: TableColumn<Row>[] = [
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
      const o = row.original as Row
      if (!o.cover) return ''
      return h(UAvatar, { src: coverUrl(o.cover), alt: o.name, size: 'lg', class: 'cover' })
    }
  },
  {
    accessorKey: 'name',
    header: '曲名',
    cell: ({ row }) => {
      const o = row.original as Row
      const dx = isDX(o.musicId)
      return h('div', { class: 't' }, [
        o.name,
        h('span', { class: ['badge', dx ? 'dx' : 'std'] }, dx ? 'DX' : 'STD')
      ])
    }
  },
  {
    accessorKey: 'tier',
    header: '难度',
    cell: ({ row }) => {
      const o = row.original as Row
      return h('span', { class: ['badge', `tier-${o.tier}`] }, `${TIER_LABEL[o.tier]} ${o.rate}`)
    }
  },
  {
    id: 'stat',
    header: '游玩占比(平均/中位/众数)',
    cell: ({ row }) => {
      const o = row.original as Row
      return h('div', { class: 's' }, `平均 ${o.avg}% · 中位 ${o.med}% · 众数 ${o.mode}%`)
    }
  },
  {
    accessorKey: 'players',
    header: '游玩人数',
    meta: { class: { th: 'text-right', td: 'text-right' } },
    cell: ({ row }) => h('div', { class: 'plays' }, fmtNum(row.getValue('players') as number))
  }
]

async function load() {
  loading.value = true
  error.value = ''
  try {
    const d = await apiGet<{ totalPlayers: number; songs: Chart[] }>('/api/chart145')
    total.value = d.totalPlayers
    songs.value = d.songs.map(s => ({ ...s, tier: resolveTier(s.musicId, s.level, s.rate) }))
  } catch (e: any) {
    error.value = e?.message || '网络错误'
  } finally {
    loading.value = false
  }
}
onMounted(load)
</script>

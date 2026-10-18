<!-- 全站统一页壳:所有 pages/ 页面都用它包裹,不再各自写 wrap/h2/loading -->
<script setup lang="ts">
withDefaults(defineProps<{
  title?: string
  description?: string
  loading?: boolean
  error?: string
}>(), {
  title: '',
  description: '',
  loading: false,
  error: ''
})

defineEmits<{ retry: [] }>()
</script>

<template>
  <div class="wrap">
    <div v-if="title || $slots.actions" class="ds-head">
      <div class="min-w-0">
        <h2 v-if="title" class="page">{{ title }}</h2>
        <p v-if="description" class="page-sub">{{ description }}</p>
      </div>
      <div v-if="$slots.actions" class="ds-actions">
        <slot name="actions" />
      </div>
    </div>

    <slot name="alert" />

    <div v-if="loading" class="space-y-3">
      <UCard><USkeleton class="h-24 w-full" /></UCard>
      <UCard><USkeleton class="h-40 w-full" /></UCard>
    </div>

    <UAlert
      v-else-if="error"
      color="error"
      variant="soft"
      icon="i-lucide-triangle-alert"
      title="加载失败"
      :description="error"
    >
      <template #actions>
        <UButton color="error" variant="solid" icon="i-lucide-rotate-cw" @click="$emit('retry')">
          重试
        </UButton>
      </template>
    </UAlert>

    <slot v-else />
  </div>
</template>

<!-- 我要上报弹窗:内嵌上报向导(与 /report 同一组件),打开即全新实例 -->
<template>
  <UModal
    v-model:open="open"
    title="我要上报"
    description="机台异常手动上报,数据直达 isMaiDown。"
  >
    <template #body>
      <ReportWizard v-if="open" @done="open = false" />
    </template>
  </UModal>
</template>

<script setup lang="ts">
const open = defineModel<boolean>('open', { default: false })
</script>

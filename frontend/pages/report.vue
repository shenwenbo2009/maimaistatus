<template>
  <DesignSystem
    title="我要上报"
    description="机台异常手动上报,数据直达 isMaiDown。经网关透传上游,签名在本地计算。"
  >
    <UCard>
      <ReportWizard @done="() => navigateTo('/')" />
    </UCard>
  </DesignSystem>
</template>

<!-- 我要上报向导(3 步):选类型 → 填内容 → 验证码确认,经网关透传上游 /bot-post -->
<!-- 被报告页与上报弹窗共同复用,单源维护 -->
<script setup lang="ts">
import type { StepperItem } from '@nuxt/ui'

defineEmits<{ done: [] }>()

interface ReportOption { t: number; label: string; icon: string; gold?: boolean }

// 上游 t 代码见 https://mai.chongxi.us/docs/api-reference/report
// 恢复类上游只定义了 501(服务器恢复正常),4 个恢复项统一用 501,靠显示名称区分
const FAULTS: ReportOption[] = [
  { t: 101, label: '机台断网', icon: 'i-lucide-wifi-off' },
  { t: 102, label: '机台登录变游客', icon: 'i-lucide-user-x' },
  { t: 103, label: '舞萌NET打不开', icon: 'i-lucide-cloud-off' },
  { t: 104, label: '无法获取二维码', icon: 'i-lucide-qr-code' },
  { t: 105, label: '成绩上传失败', icon: 'i-lucide-upload' },
  { t: 202, label: '账号进小黑屋', icon: 'i-lucide-ban', gold: true }
]
const RECOVERS: ReportOption[] = [
  { t: 501, label: '机台绿网', icon: 'i-lucide-wifi' },
  { t: 501, label: '二维码正常', icon: 'i-lucide-qr-code' },
  { t: 501, label: '公众号正常', icon: 'i-lucide-message-circle' },
  { t: 501, label: '登录正常', icon: 'i-lucide-log-in' }
]

const steps: StepperItem[] = [
  { title: '选择类型', icon: 'i-lucide-list-checks' },
  { title: '填写内容', icon: 'i-lucide-keyboard' },
  { title: '验证码确认', icon: 'i-lucide-shield-check' }
]

const step = ref(0)
const selected = ref<ReportOption | null>(null)
const count = ref<number>(1)
const displayName = ref('')
const captchaId = ref('')
const captchaCode = ref('')
const captchaInput = ref('')
const captchaLoading = ref(false)
const captchaError = ref('')
const sending = ref(false)
const result = ref<{ ok: boolean; msg: string } | null>(null)

// 上报签名密钥:站长在 frontend/.env.example → 环境变量中配置,见 nuxt.config.ts
const { reportClientId, reportPrivateKey } = useRuntimeConfig().public
const keysReady = computed(() => {
  const id = String(reportClientId || '')
  const key = String(reportPrivateKey || '')
  return !!id && !!key && !id.includes('<your-key>') && !key.includes('<your-key>')
})

function reset() {
  step.value = 0
  selected.value = null
  count.value = 1
  displayName.value = ''
  captchaId.value = ''
  captchaCode.value = ''
  captchaInput.value = ''
  captchaError.value = ''
  result.value = null
}

function isSel(o: ReportOption) {
  return selected.value?.t === o.t && selected.value?.label === o.label
}

async function sha256Hex(text: string): Promise<string> {
  const buf = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(text))
  return [...new Uint8Array(buf)].map(b => b.toString(16).padStart(2, '0')).join('')
}

async function hmacHex(key: string, text: string): Promise<string> {
  const cryptoKey = await crypto.subtle.importKey(
    'raw', new TextEncoder().encode(key), { name: 'HMAC', hash: 'SHA-256' }, false, ['sign']
  )
  const sig = await crypto.subtle.sign('HMAC', cryptoKey, new TextEncoder().encode(text))
  return [...new Uint8Array(sig)].map(b => b.toString(16).padStart(2, '0')).join('')
}

async function ensureCaptcha(force = false) {
  if (captchaId.value && !force) return
  captchaLoading.value = true
  captchaError.value = ''
  captchaId.value = ''
  captchaCode.value = ''
  try {
    const d = await $fetch<{ captcha_id: string; code: string }>('/api/report/captcha', {
      baseURL: useRuntimeConfig().public.apiBase as string
    })
    captchaId.value = d.captcha_id
    captchaCode.value = d.code
  } catch (e: any) {
    captchaError.value = '网关未连接:请先启动 Go 后端再上报。'
  } finally {
    captchaLoading.value = false
  }
}

function goStep2() {
  step.value = 2
  ensureCaptcha()
}

async function submit() {
  result.value = null
  if (!selected.value) return
  if (captchaInput.value.trim() !== captchaCode.value || !captchaId.value) {
    result.value = { ok: false, msg: '验证码不一致,请照着上方数字重新输入。' }
    return
  }
  sending.value = true
  try {
    const name = displayName.value.trim() || selected.value.label
    const body = [{ t: selected.value.t, v: Math.floor(count.value), r: 'WEB', bot_display_name: name }]
    const bodyText = JSON.stringify(body)
    const ts = String(Date.now())
    const bodyHash = await sha256Hex(bodyText)
    const signature = await hmacHex(
      String(reportPrivateKey), `POST:/bot-post:${String(reportClientId)}:${ts}:${bodyHash}`
    )
    await $fetch('/api/report', {
      baseURL: useRuntimeConfig().public.apiBase as string,
      method: 'POST',
      body: bodyText,
      headers: {
        'Content-Type': 'application/json',
        'X-Client-ID': String(reportClientId),
        'X-Timestamp': ts,
        'X-Signature': signature,
        'X-Captcha-ID': captchaId.value,
        'X-Captcha-Code': captchaInput.value.trim()
      }
    })
    result.value = { ok: true, msg: `已上报「${name}」× ${Math.floor(count.value)},感谢反馈!` }
  } catch (e: any) {
    const code = e?.response?.status ?? e?.statusCode
    if (code === 403) { // 验证码失效:清掉 forcing 换一张
      captchaId.value = ''
      captchaCode.value = ''
    }
    result.value = {
      ok: false,
      msg: code === 401 ? '身份验证失败:检查上报密钥配置。'
        : code === 403 ? '验证码已失效,请点换一张重试。'
        : code === 429 ? '触发限流:请 30 秒后再试。'
        : (e?.data?.error || e?.message || '上报失败,请稍后重试。')
    }
  } finally {
    sending.value = false
  }
}
</script>

<template>
  <div class="space-y-4">
    <UAlert
      v-if="!keysReady"
      color="warning"
      variant="soft"
      icon="i-lucide-triangle-alert"
      title="站长未配置上报密钥"
      description="请在 frontend/.env 中填写 NUXT_PUBLIC_REPORT_CLIENT_ID / NUXT_PUBLIC_REPORT_PRIVATE_KEY 后重启前端。"
    />

    <div v-else class="space-y-4">
      <UStepper v-model="step" :items="steps" disabled size="sm" class="w-full" />

      <!-- 第一步:选择上报类型 -->
      <div v-if="step === 0" class="space-y-3">
        <div>
          <p class="dim mb-2">故障</p>
          <div class="grid grid-cols-2 sm:grid-cols-3 gap-2">
            <UButton
              v-for="o in FAULTS"
              :key="o.label"
              :icon="o.icon"
              :variant="isSel(o) ? 'solid' : 'soft'"
              :color="isSel(o) ? 'primary' : 'neutral'"
              :class="['justify-start cursor-pointer', o.gold && !isSel(o) ? 'type-202' : '']"
              @click="selected = o"
            >
              {{ o.label }}
            </UButton>
          </div>
        </div>
        <div>
          <p class="dim mb-2">恢复</p>
          <div class="grid grid-cols-2 sm:grid-cols-3 gap-2">
            <UButton
              v-for="o in RECOVERS"
              :key="o.label"
              :icon="o.icon"
              :variant="isSel(o) ? 'solid' : 'soft'"
              :color="isSel(o) ? 'success' : 'neutral'"
              class="justify-start cursor-pointer"
              @click="selected = o"
            >
              {{ o.label }}
            </UButton>
          </div>
        </div>
      </div>

      <!-- 第二步:次数 + 显示名称 -->
      <div v-else-if="step === 1" class="space-y-4">
        <UAlert
          v-if="selected"
          color="neutral"
          variant="soft"
          :icon="selected.icon"
          :title="`上报:${selected.label}`"
          :description="`类型代码 t=${selected.t}`"
        />
        <UFormField label="上传次数" required>
          <UInput v-model.number="count" type="number" :min="1" class="w-full" />
        </UFormField>
        <UFormField label="显示名称" :hint="`空则与上报类型名称一致`">
          <UInput v-model="displayName" :placeholder="selected?.label || '显示名称'" class="w-full" />
        </UFormField>
      </div>

      <!-- 第三步:验证码确认 + 上报 -->
      <div v-else class="space-y-4">
        <UAlert
          v-if="selected"
          color="neutral"
          variant="soft"
          :icon="selected.icon"
          :title="`上报:${selected.label} × ${Math.floor(count || 0)}`"
          :description="`显示为「${displayName.trim() || selected.label}」`"
        />
        <div>
          <p class="dim mb-2">重复输入下方 8 位数字验证码(后端随机生成)</p>
          <div class="flex items-center gap-3 flex-wrap">
            <div class="font-mono text-3xl font-black tracking-[0.35em] px-4 py-2 rounded-xl bg-elevated select-all">
              {{ captchaCode || '········' }}
            </div>
            <UButton
              icon="i-lucide-rotate-ccw"
              variant="ghost"
              size="sm"
              class="cursor-pointer"
              :loading="captchaLoading"
              @click="ensureCaptcha(true)"
            >
              换一张
            </UButton>
          </div>
        </div>
        <UInput
          v-model="captchaInput"
          placeholder="输入 8 位数字验证码"
          maxlength="8"
          inputmode="numeric"
          class="w-full"
        />
        <UAlert
          v-if="captchaError"
          color="error"
          variant="soft"
          icon="i-lucide-triangle-alert"
          title="验证码获取失败"
          :description="captchaError"
        />
      </div>

      <UAlert
        v-if="result"
        :color="result.ok ? 'success' : 'error'"
        variant="soft"
        :icon="result.ok ? 'i-lucide-check-circle' : 'i-lucide-triangle-alert'"
        :title="result.ok ? '上报成功' : '上报失败'"
        :description="result.msg"
      />

      <!-- 步骤控制:全 Nuxt UI Button -->
      <div class="flex items-center justify-between gap-2">
        <UButton
          v-if="step > 0 && !result?.ok"
          leading-icon="i-lucide-arrow-left"
          color="neutral"
          variant="ghost"
          class="cursor-pointer"
          @click="step--"
        >
          上一步
        </UButton>
        <span v-else />
        <div class="flex gap-2">
          <UButton color="neutral" variant="ghost" class="cursor-pointer" @click="$emit('done')">
            {{ result?.ok ? '完成' : '取消' }}
          </UButton>
          <UButton
            v-if="step === 0"
            trailing-icon="i-lucide-arrow-right"
            class="cursor-pointer"
            :disabled="!selected"
            @click="step++"
          >
            下一步
          </UButton>
          <UButton
            v-else-if="step === 1"
            trailing-icon="i-lucide-arrow-right"
            class="cursor-pointer"
            :disabled="!Number.isFinite(count) || count < 1"
            @click="goStep2"
          >
            下一步
          </UButton>
          <UButton
            v-else-if="!result?.ok"
            color="warning"
            icon="i-lucide-send"
            class="cursor-pointer"
            :loading="sending || captchaLoading"
            @click="submit"
          >
            提交上报
          </UButton>
          <UButton
            v-else
            color="neutral"
            variant="soft"
            icon="i-lucide-rotate-ccw"
            class="cursor-pointer"
            @click="reset(); ensureCaptcha(true)"
          >
            再报一次
          </UButton>
        </div>
      </div>

      <p class="dim">签名在本地浏览器计算,密钥不出本机(经网关透传上游)。请勿频繁提交,建议 30 秒聚合一次。</p>
    </div>
  </div>
</template>

<!-- 我要上报向导(3 步):选类型 → 填内容 → 验证确认,经网关透传上游 /bot-post -->
<!-- 验证步双模式:Corptcha 人机验证组件(主通道) / 本地 8 位数字验证码(降级通道,长期并存) -->
<!-- 被报告页与上报弹窗共同复用,单源维护 -->
<script setup lang="ts">
import type { StepperItem } from '@nuxt/ui'

defineEmits<{ done: [] }>()

interface ReportOption { t: number; label: string; icon: string; gold?: boolean }

// Corptcha SDK 类型声明(零 any;SDK 可选方法用可选链调用,不假设存在)
declare global {
  interface CorptchaRenderOptions {
    apiBaseUrl: string
    siteKey: string
    purpose: string
    language?: string
    theme?: { mode?: string }
    onSuccess?: (token: string) => void
    onError?: (error: { errorCode?: string; message?: string }) => void
    onExpired?: () => void
  }
  interface CorptchaWidget {
    execute?: () => void
    destroy?: () => void
  }
  interface CorptchaSDK {
    render(container: HTMLElement, options: CorptchaRenderOptions): CorptchaWidget
  }
  interface Window {
    Corptcha?: CorptchaSDK
  }
}

const CORPTCHA_SDK_URL = 'https://res.25y.cn/corptcha/corptcha.iife.js'
const CORPTCHA_API_BASE = 'https://cpt-api.25y.cn'
const CORPTCHA_SDK_TIMEOUT = 10000

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

// 验证步双模式状态:corptcha(主通道) / local(降级+纯本地) / unknown(协商中)
const verifyMode = ref<'corptcha' | 'local' | 'unknown'>('unknown')
const modeDegraded = ref(false)
const cptToken = ref('')
const cptBusy = ref(false)
const cptNotice = ref('')
const widgetContainer = ref<HTMLElement | null>(null)

// 模块级非响应式:widget 实例与 SDK 加载单例(弹窗多次开关不重复注入 script)
let widgetInstance: CorptchaWidget | null = null
let sdkPromise: Promise<CorptchaSDK> | null = null
// purpose 由后端 /api/report/mode 下发(单一事实源,防前后端漂移)
let corptchaPurpose = 'report'

// 上报签名密钥:站长在 frontend/.env.example → 环境变量中配置,见 nuxt.config.ts
const { reportClientId, reportPrivateKey, corptchaSiteKey } = useRuntimeConfig().public
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
  verifyMode.value = 'unknown'
  modeDegraded.value = false
  cptToken.value = ''
  cptNotice.value = ''
  widgetInstance = null
}

// ReportModal 以 v-if 挂载向导,弹窗关闭即销毁:主动 destroy 兜底防泄漏
onBeforeUnmount(() => {
  widgetInstance?.destroy?.()
  widgetInstance = null
})

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

// 查询验证方式(mode 协商);网关不可达等任何失败兜底 local
async function fetchVerifyMode(): Promise<{ mode: string; degraded: boolean }> {
  try {
    const d = await $fetch<{ mode: string; degraded: boolean; purpose: string }>('/api/report/mode', {
      baseURL: useRuntimeConfig().public.apiBase as string
    })
    if (d.purpose) corptchaPurpose = d.purpose
    return { mode: d.mode, degraded: !!d.degraded }
  } catch {
    return { mode: 'local', degraded: false }
  }
}

// 动态懒加载 SDK(仅验证步进入 corptcha 模式时注入,前两步与监控页零影响);
// 10s 超时/onerror 均复位单例,允许下次重试
function loadCorptchaSdk(): Promise<CorptchaSDK> {
  if (window.Corptcha) return Promise.resolve(window.Corptcha)
  if (sdkPromise) return sdkPromise
  sdkPromise = new Promise<CorptchaSDK>((resolve, reject) => {
    const el = document.createElement('script')
    el.src = CORPTCHA_SDK_URL
    el.async = true
    const timer = window.setTimeout(() => {
      cleanup()
      sdkPromise = null
      reject(new Error('corptcha sdk load timeout'))
    }, CORPTCHA_SDK_TIMEOUT)
    const cleanup = () => {
      window.clearTimeout(timer)
      el.onload = null
      el.onerror = null
    }
    el.onload = () => {
      cleanup()
      if (window.Corptcha) resolve(window.Corptcha)
      else {
        sdkPromise = null
        reject(new Error('corptcha sdk missing global'))
      }
    }
    el.onerror = () => {
      cleanup()
      sdkPromise = null
      reject(new Error('corptcha sdk load failed'))
    }
    document.head.appendChild(el)
  })
  return sdkPromise
}

// 渲染验证组件;容器由 v-if 控制,刚挂载时须 nextTick 等 DOM 就绪再取 ref
async function renderCorptchaWidget() {
  if (!widgetContainer.value || !window.Corptcha) return
  widgetInstance?.destroy?.()
  widgetInstance = null
  widgetInstance = window.Corptcha.render(widgetContainer.value, {
    apiBaseUrl: CORPTCHA_API_BASE,
    siteKey: String(corptchaSiteKey || ''),
    purpose: corptchaPurpose,
    language: 'zh-CN',
    theme: { mode: 'auto' },
    onSuccess: (token: string) => {
      cptToken.value = token
      cptNotice.value = ''
    },
    onExpired: () => {
      // 第一行先清 token(提交按钮 disabled 联动),再提示重验——
      // 顺序颠倒会让用户在过期瞬间提交失效 token
      cptToken.value = ''
      cptNotice.value = '验证已过期,请重新完成人机验证。'
      widgetInstance?.execute?.()
    },
    onError: () => {
      fallbackToLocal('人机验证组件运行异常,已切换为数字验证码。')
    }
  })
}

// 降级切换到本地照打:只换验证形态,严禁清空用户已填内容(selected/count/displayName)
function fallbackToLocal(notice: string) {
  verifyMode.value = 'local'
  cptToken.value = ''
  widgetInstance = null // 容器随 v-if 卸载,置空防悬挂调用
  cptNotice.value = notice
  ensureCaptcha()
}

async function goStep2() {
  step.value = 2
  const siteKey = String(corptchaSiteKey || '')
  const m = await fetchVerifyMode()
  modeDegraded.value = m.degraded
  if (m.mode !== 'corptcha') {
    if (m.degraded) {
      fallbackToLocal('人机验证服务暂不可用,已切换为数字验证码。')
    } else {
      // 纯本地模式(站长未启用):现状体验,无额外提示
      verifyMode.value = 'local'
      ensureCaptcha()
    }
    return
  }
  if (!siteKey) {
    // 后端启用但前端未配置 siteKey:降级照打并提示(前后端配置不一致兜底)
    fallbackToLocal('站长未配置 Corptcha Site Key,已切换为数字验证码。')
    return
  }
  verifyMode.value = 'corptcha'
  cptBusy.value = true
  try {
    await loadCorptchaSdk()
    await nextTick()
    await renderCorptchaWidget()
  } catch {
    fallbackToLocal('人机验证组件加载失败,已切换为数字验证码。')
  } finally {
    cptBusy.value = false
  }
}

async function submit() {
  result.value = null
  if (!selected.value) return
  if (verifyMode.value === 'corptcha') {
    if (!cptToken.value) {
      result.value = { ok: false, msg: '请先完成人机验证。' }
      return
    }
  } else if (captchaInput.value.trim() !== captchaCode.value || !captchaId.value) {
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
    const headers: Record<string, string> = {
      'Content-Type': 'application/json',
      'X-Client-ID': String(reportClientId),
      'X-Timestamp': ts,
      'X-Signature': signature
    }
    if (verifyMode.value === 'corptcha') {
      // corptcha 通道只带 token 头,不带本地两头(后端 token 优先,混带误导排障日志)
      headers['X-Corptcha-Token'] = cptToken.value
    } else {
      headers['X-Captcha-ID'] = captchaId.value
      headers['X-Captcha-Code'] = captchaInput.value.trim()
    }
    // retry:0 —— 显式禁用框架对 POST 的重试,防止 token 被静默二次消耗
    await $fetch('/api/report', {
      baseURL: useRuntimeConfig().public.apiBase as string,
      method: 'POST',
      body: bodyText,
      headers,
      retry: 0
    })
    result.value = { ok: true, msg: `已上报「${name}」× ${Math.floor(count.value)},感谢反馈!` }
  } catch (e: any) {
    const code = e?.response?.status ?? e?.statusCode
    if (verifyMode.value === 'corptcha') {
      if (code === 403) {
        // 验证未通过:清 token 重置组件,不携带同一 token 自动重试
        cptToken.value = ''
        cptNotice.value = '验证未通过,请重新完成人机验证。'
        widgetInstance?.execute?.()
        result.value = { ok: false, msg: '人机验证未通过,已重置验证,请完成后重试。' }
        return
      }
      if (code === 503 && e?.data?.fallback === 'captcha') {
        // 服务故障降级:切本地照打,保留已填内容,补照打重新提交
        fallbackToLocal('人机验证服务暂不可用,已切换为数字验证码。')
        result.value = { ok: false, msg: '人机验证服务暂不可用,请用数字验证码重新提交。' }
        return
      }
    } else if (code === 403) {
      captchaId.value = ''
      captchaCode.value = ''
    }
    result.value = {
      ok: false,
      msg: code === 401 ? '身份验证失败:检查上报密钥配置。'
        : (verifyMode.value !== 'corptcha' && code === 403) ? '验证码已失效,请点换一张重试。'
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

      <!-- 第三步:验证确认 + 上报 -->
      <div v-else class="space-y-4">
        <UAlert
          v-if="selected"
          color="neutral"
          variant="soft"
          :icon="selected.icon"
          :title="`上报:${selected.label} × ${Math.floor(count || 0)}`"
          :description="`显示为「${displayName.trim() || selected.label}」`"
        />
        <!-- Corptcha 人机验证(主通道) -->
        <div v-if="verifyMode === 'corptcha'" class="space-y-3">
          <p class="dim mb-2">完成下方人机验证后提交上报</p>
          <div ref="widgetContainer" class="min-h-24"></div>
          <UAlert
            v-if="cptBusy"
            color="neutral"
            variant="soft"
            icon="i-lucide-loader-circle"
            title="正在加载人机验证组件..."
          />
          <UAlert
            v-if="cptNotice"
            color="warning"
            variant="soft"
            icon="i-lucide-triangle-alert"
            title="验证提示"
            :description="cptNotice"
          />
        </div>
        <!-- 本地 8 位数字验证码(降级通道,现状 UI 原样复用) -->
        <template v-else-if="verifyMode === 'local'">
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
        </template>
        <!-- mode 协商中 -->
        <div v-else class="dim">
          正在获取验证方式...
        </div>
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
            :disabled="verifyMode === 'corptcha' && !cptToken"
            :loading="sending || (verifyMode !== 'corptcha' && captchaLoading) || cptBusy"
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
            @click="reset()"
          >
            再报一次
          </UButton>
        </div>
      </div>

      <p class="dim">签名在本地浏览器计算,密钥不出本机(经网关透传上游)。请勿频繁提交,建议 30 秒聚合一次。</p>
    </div>
  </div>
</template>

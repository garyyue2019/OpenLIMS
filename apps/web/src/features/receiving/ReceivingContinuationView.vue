<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { authSnapshot } from '../../auth-store'
import IdentityAssessmentPanel from './IdentityAssessmentPanel.vue'
import ReceivingExceptionPanel from './ReceivingExceptionPanel.vue'
import ReceivingReleasePanel from './ReceivingReleasePanel.vue'
import type { ReceivingReleaseState } from './release-client'

type ItemState = 'QUARANTINED' | ReceivingReleaseState
interface ContinuationTarget {
  receivedItemId: string
  itemVersion: number
  itemState: ItemState
  exceptionId?: string
}

const route = useRoute()
const router = useRouter()
const form = reactive({
  receivedItemId: '', itemVersion: 1, itemState: 'QUARANTINED' as ItemState, exceptionId: ''
})
const target = ref<ContinuationTarget>()
const validationError = ref('')
const authenticated = computed(() => authSnapshot.value.status === 'authenticated')

onMounted(hydrateRoute)
watch(() => route.fullPath, hydrateRoute)

function openTarget(): void {
  const receivedItemId = form.receivedItemId.trim()
  const exceptionId = form.exceptionId.trim()
  if (!authenticated.value) {
    validationError.value = '请先登录后再打开既有实物。'
    return
  }
  if (!receivedItemId || !positiveInteger(form.itemVersion) || !validState(form.itemState)) {
    validationError.value = '需要稳定 receivedItemId、正整数 itemVersion 和批准的 itemState。'
    return
  }
  validationError.value = ''
  target.value = {
    receivedItemId, itemVersion: form.itemVersion, itemState: form.itemState,
    ...(exceptionId ? { exceptionId } : {})
  }
  syncRoute()
}

function closeTarget(): void {
  target.value = undefined
  validationError.value = ''
  void router.replace({ name: 'receiving.continuation' })
}

function updateItemVersion(version: number): void {
  if (!target.value || !positiveInteger(version)) return
  target.value.itemVersion = version
  form.itemVersion = version
  syncRoute()
}

function updateItemState(state: ReceivingReleaseState): void {
  if (!target.value) return
  target.value.itemState = state
  form.itemState = state
  syncRoute()
}

function hydrateRoute(): void {
  const receivedItemId = routeValue(route.params.receivedItemId)
  if (!receivedItemId) return
  const itemVersion = Number(routeValue(route.query.itemVersion))
  const itemState = routeValue(route.query.itemState)
  const exceptionId = routeValue(route.query.exceptionId)
  form.receivedItemId = receivedItemId
  form.itemVersion = Number.isFinite(itemVersion) ? itemVersion : 0
  if (validState(itemState)) form.itemState = itemState
  form.exceptionId = exceptionId
  if (!positiveInteger(form.itemVersion) || !validState(itemState)) {
    target.value = undefined
    validationError.value = '深链接缺少正整数 itemVersion 或批准的 itemState，请补齐后打开。'
    return
  }
  validationError.value = ''
  target.value = {
    receivedItemId, itemVersion: form.itemVersion, itemState,
    ...(exceptionId ? { exceptionId } : {})
  }
}

function syncRoute(): void {
  if (!target.value) return
  void router.replace({
    name: 'receiving.item-continuation',
    params: { receivedItemId: target.value.receivedItemId },
    query: {
      itemVersion: String(target.value.itemVersion),
      itemState: target.value.itemState,
      ...(target.value.exceptionId ? { exceptionId: target.value.exceptionId } : {})
    }
  })
}

function routeValue(value: unknown): string {
  if (Array.isArray(value)) return typeof value[0] === 'string' ? value[0] : ''
  return typeof value === 'string' ? value : ''
}
function positiveInteger(value: number): boolean { return Number.isInteger(value) && value > 0 }
function validState(value: unknown): value is ItemState {
  return typeof value === 'string' && ['QUARANTINED', 'ACCEPTED', 'CONDITIONALLY_ACCEPTED'].includes(value)
}
</script>

<template>
  <main class="receiving-page continuation-page">
    <header>
      <p class="eyebrow">RECEIVING · CONTINUATION · DEV-037</p>
      <h1>既有实物续办</h1>
      <p>用稳定实物 ID 和精确对象版本重新打开身份、异常与放行面板；不会再次登记收样，也不会猜测最新版本。</p>
    </header>

    <a-alert
      v-if="!authenticated"
      type="warning"
      show-icon
      message="请先登录"
      description="续办入口不会在匿名状态探测对象是否存在。"
    />

    <form class="form-section continuation-form" @submit.prevent="openTarget">
      <h2>打开既有实物</h2>
      <div class="field-grid">
        <label>ReceivedItem ID<input v-model="form.receivedItemId" required :disabled="!authenticated"></label>
        <label>当前对象版本<input v-model.number="form.itemVersion" type="number" min="1" step="1" required :disabled="!authenticated"></label>
        <label>当前对象状态<select v-model="form.itemState" :disabled="!authenticated"><option value="QUARANTINED">QUARANTINED</option><option value="ACCEPTED">ACCEPTED</option><option value="CONDITIONALLY_ACCEPTED">CONDITIONALLY_ACCEPTED</option></select></label>
        <label>Exception ID（可选）<input v-model="form.exceptionId" :disabled="!authenticated"></label>
      </div>
      <p>版本和状态必须来自受信工作清单或前一次服务器响应；页面不读取私表或选择“最新版”。</p>
      <p v-if="validationError" class="continuation-error" role="alert">{{ validationError }}</p>
      <div class="form-actions">
        <a-button type="primary" html-type="submit" :disabled="!authenticated">打开工作区</a-button>
        <a-button v-if="target" @click="closeTarget">关闭工作区</a-button>
      </div>
    </form>

    <section v-if="target" class="continuation-target" aria-live="polite">
      <header class="target-summary">
        <div><span>实物</span><strong>{{ target.receivedItemId }}</strong></div>
        <div><span>固定版本</span><strong>v{{ target.itemVersion }}</strong></div>
        <div><span>状态</span><strong>{{ target.itemState }}</strong></div>
        <div><span>异常</span><strong>{{ target.exceptionId || '新建或不续办异常' }}</strong></div>
      </header>

      <IdentityAssessmentPanel
        :key="`identity-${target.receivedItemId}`"
        :received-item-id="target.receivedItemId"
        @item-version-changed="updateItemVersion"
      />
      <ReceivingExceptionPanel
        :key="`exception-${target.receivedItemId}-${target.exceptionId || 'new'}`"
        :received-item-id="target.receivedItemId"
        :item-version="target.itemVersion"
        :exception-id="target.exceptionId"
        @item-version-changed="updateItemVersion"
      />
      <ReceivingReleasePanel
        :key="`release-${target.receivedItemId}-${target.itemState}`"
        :received-item-id="target.receivedItemId"
        :item-version="target.itemVersion"
        :item-state="target.itemState"
        @item-version-changed="updateItemVersion"
        @item-state-changed="updateItemState"
      />
    </section>
  </main>
</template>

<style scoped>
.continuation-page { display: grid; gap: 1rem; }
.continuation-form { display: grid; gap: 1rem; }
.continuation-target { display: grid; gap: 1rem; }
.target-summary { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 1px; border: 1px solid #d9d9d9; background: #d9d9d9; }
.target-summary div { display: grid; gap: .35rem; min-width: 0; padding: .85rem; background: #fff; }
.target-summary span { color: #595959; font-size: .82rem; }
.target-summary strong { overflow-wrap: anywhere; }
.continuation-error { color: #a8071a; font-weight: 600; }
@media (max-width: 760px) { .target-summary { grid-template-columns: 1fr 1fr; } }
</style>

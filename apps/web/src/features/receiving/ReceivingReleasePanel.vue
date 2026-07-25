<script setup lang="ts">
import { computed, ref } from 'vue'
import { authSnapshot } from '../../auth-store'
import { canApproveReceivingRelease } from './release-access'
import {
  ReceivingReleaseApiError,
  releaseRuleSetVersion,
  submitReceivingReleaseDecision,
  type ReceivingReleaseDecisionResult,
  type ReceivingReleaseState
} from './release-client'

const props = defineProps<{
  receivedItemId: string
  itemVersion: number
  itemState: 'QUARANTINED' | ReceivingReleaseState
}>()
const emit = defineEmits<{
  itemVersionChanged: [version: number]
  itemStateChanged: [state: ReceivingReleaseState]
}>()
const rationale = ref('')
const result = ref<ReceivingReleaseDecisionResult>()
const busy = ref(false)
const errorCode = ref<string>()
const profile = computed(() => authSnapshot.value.user?.profile as Record<string, unknown> | undefined)
const canRelease = computed(() =>
  props.itemState === 'QUARANTINED' &&
  authSnapshot.value.status === 'authenticated' &&
  canApproveReceivingRelease(profile.value))

async function release(): Promise<void> {
  const token = authSnapshot.value.user?.access_token
  if (!canRelease.value || !token || busy.value) return
  busy.value = true
  errorCode.value = undefined
  try {
    result.value = await submitReceivingReleaseDecision(props.receivedItemId, {
      expectedItemVersion: props.itemVersion,
      ruleSetVersion: releaseRuleSetVersion,
      rationale: rationale.value.trim()
    }, token)
    emit('itemVersionChanged', result.value.itemVersion)
    emit('itemStateChanged', result.value.state)
  } catch (error) {
    errorCode.value = error instanceof ReceivingReleaseApiError ? error.errorCode : 'RELEASE_NETWORK_ERROR'
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <section class="release-panel" :aria-labelledby="`release-${receivedItemId}`">
    <h4 :id="`release-${receivedItemId}`">受控放行 · DEV-007</h4>
    <a-alert
      v-if="!result"
      type="info"
      show-icon
      message="服务端锁内重新校验身份、全部异常和版本"
      description="只有 MATCHED 且无阻断异常时才会放行；条件接收限制由服务端取交集/并集，UNKNOWN 保持隔离。"
    />
    <form v-if="!result && itemState === 'QUARANTINED'" @submit.prevent="release">
      <label>质量放行理由
        <textarea v-model="rationale" required :disabled="!canRelease || busy" />
      </label>
      <p>规则：{{ releaseRuleSetVersion }} · 单一质量批准，无额外多级签署</p>
      <a-button type="primary" html-type="submit" :loading="busy" :disabled="!canRelease || !rationale.trim()">
        提交受控放行
      </a-button>
    </form>
    <article v-if="result" aria-live="polite">
      <p>结果：<strong>{{ result.outcome }}</strong> · 状态 {{ result.state }} · 决定 v{{ result.version }}</p>
      <p>身份决定：{{ result.identityDecisionId }}@{{ result.identityDecisionVersion }}</p>
      <p>允许动作：{{ result.allowedActions.join(', ') || '无' }}</p>
      <p>禁止动作：{{ result.prohibitedActions.join(', ') || '无' }}</p>
      <p v-if="result.constraintsValidUntil">限制有效至：{{ result.constraintsValidUntil }}</p>
      <p>固定异常决定：{{ result.exceptionDecisionVersions.length }} 项 · 批准人 {{ result.approvedBy }}</p>
    </article>
    <a-alert
      v-if="errorCode"
      type="error"
      show-icon
      :message="`放行失败：${errorCode}`"
      description="失败不会改变隔离状态，也不会发布成功事件；请刷新身份、异常和对象版本后重试。"
    />
  </section>
</template>

<style scoped>
.release-panel { margin: 1rem 0; padding: 1rem; border: 1px solid #1677ff; border-radius: .5rem; }
.release-panel form { display: grid; gap: .65rem; margin-top: 1rem; }
.release-panel label { display: grid; gap: .25rem; }
</style>

<script setup lang="ts">
import { computed, reactive, ref } from 'vue'
import { authSnapshot } from '../../auth-store'
import { canCreateException, canEhsApproveException, canQualityApproveException } from './exception-access'
import {
  createReceivingException,
  exceptionMatrixVersion,
  ReceivingExceptionApiError,
  submitReceivingExceptionDecision,
  type ReceivingExceptionDecisionType,
  type ReceivingExceptionResult,
  type ReceivingExceptionType
} from './exception-client'

const props = defineProps<{ receivedItemId: string, itemVersion: number }>()
const emit = defineEmits<{ itemVersionChanged: [version: number] }>()
const current = ref<ReceivingExceptionResult>()
const busy = ref(false)
const errorCode = ref<string>()
const profile = computed(() => authSnapshot.value.user?.profile as Record<string, unknown> | undefined)
const canCreate = computed(() => authSnapshot.value.status === 'authenticated' && canCreateException(profile.value))
const canApprove = computed(() => current.value?.severity === 'SAFETY_CRITICAL'
  ? canEhsApproveException(profile.value)
  : canQualityApproveException(profile.value))
const createForm = reactive({
  type: 'QUANTITY_SHORTAGE' as ReceivingExceptionType,
  description: '', evidenceRef: '', evidenceHash: ''
})
const decisionForm = reactive({
  type: 'AWAIT_CUSTOMER' as ReceivingExceptionDecisionType,
  allowedActions: 'DISASSEMBLY', prohibitedActions: 'SAMPLE_PREPARATION, TEST_ASSIGNMENT',
  validUntil: '', evidenceRef: '', evidenceHash: '', technicalImpact: '', rationale: ''
})
const decisionOptions = computed<ReceivingExceptionDecisionType[]>(() =>
  current.value?.severity === 'SAFETY_CRITICAL'
    ? ['REJECT', 'SAFETY_HOLD']
    : ['AWAIT_CUSTOMER', 'CONDITIONAL_ACCEPT', 'REJECT'])

async function create(): Promise<void> {
  const token = authSnapshot.value.user?.access_token
  if (!canCreate.value || !token || busy.value) return
  busy.value = true; errorCode.value = undefined
  try {
    current.value = await createReceivingException({
      receivedItemId: props.receivedItemId, expectedItemVersion: props.itemVersion,
      type: createForm.type, observedAt: new Date().toISOString(), description: createForm.description.trim(),
      evidenceRefs: [createForm.evidenceRef.trim()].filter(Boolean),
      evidenceHashes: [createForm.evidenceHash.trim()].filter(Boolean)
    }, token)
    emit('itemVersionChanged', current.value.itemVersion)
    decisionForm.type = current.value.severity === 'SAFETY_CRITICAL' ? 'SAFETY_HOLD' : 'AWAIT_CUSTOMER'
  } catch (error) {
    errorCode.value = error instanceof ReceivingExceptionApiError ? error.errorCode : 'EXCEPTION_NETWORK_ERROR'
  } finally { busy.value = false }
}

function list(value: string): string[] { return value.split(',').map(item => item.trim()).filter(Boolean) }

async function decide(): Promise<void> {
  const token = authSnapshot.value.user?.access_token
  if (!current.value || !canApprove.value || !token || busy.value) return
  busy.value = true; errorCode.value = undefined
  const conditional = decisionForm.type === 'CONDITIONAL_ACCEPT'
  try {
    current.value = await submitReceivingExceptionDecision(current.value.exceptionId, {
      expectedVersion: current.value.version, decisionType: decisionForm.type,
      allowedActions: conditional ? list(decisionForm.allowedActions) : [],
      prohibitedActions: conditional ? list(decisionForm.prohibitedActions) : [],
      ...(conditional && decisionForm.validUntil ? { validUntil: new Date(decisionForm.validUntil).toISOString() } : {}),
      evidenceRefs: [decisionForm.evidenceRef.trim()].filter(Boolean),
      evidenceHashes: [decisionForm.evidenceHash.trim()].filter(Boolean),
      technicalImpact: conditional ? decisionForm.technicalImpact.trim() : '',
      rationale: decisionForm.rationale.trim(), matrixVersion: exceptionMatrixVersion
    }, token)
    emit('itemVersionChanged', current.value.itemVersion)
  } catch (error) {
    errorCode.value = error instanceof ReceivingExceptionApiError ? error.errorCode : 'EXCEPTION_NETWORK_ERROR'
  } finally { busy.value = false }
}
</script>

<template>
  <section class="exception-panel" :aria-labelledby="`exception-${receivedItemId}`">
    <h4 :id="`exception-${receivedItemId}`">收样异常工作台 · DEV-006</h4>
    <a-alert type="warning" show-icon message="任何异常决定仍保持 QUARANTINED" description="条件接收只记录显式限制；解除隔离必须等待 DEV-007。" />
    <form v-if="!current" @submit.prevent="create">
      <label>异常分类<select v-model="createForm.type" :disabled="!canCreate || busy">
        <option value="QUANTITY_SHORTAGE">数量不足</option><option value="TEMPERATURE_EXCURSION">超温</option>
        <option value="DAMAGED">破损</option><option value="CONTAMINATION">污染</option>
        <option value="LABEL_CONFLICT">标签冲突</option><option value="IDENTITY_MISMATCH">身份错配</option>
        <option value="IDENTITY_INDETERMINATE">身份待定</option>
      </select></label>
      <label>事实描述<textarea v-model="createForm.description" required :disabled="!canCreate || busy" /></label>
      <label>证据引用<input v-model="createForm.evidenceRef" required :disabled="!canCreate || busy"></label>
      <label>证据 SHA-256<input v-model="createForm.evidenceHash" minlength="64" maxlength="64" required :disabled="!canCreate || busy"></label>
      <a-button type="primary" html-type="submit" :loading="busy" :disabled="!canCreate">追加异常事实</a-button>
    </form>
    <template v-else>
      <p>异常：<strong>{{ current.type }}</strong> · {{ current.severity }} · 状态 {{ current.status }} · v{{ current.version }}</p>
      <form @submit.prevent="decide">
        <label>决定<select v-model="decisionForm.type" :disabled="!canApprove || busy">
          <option v-for="option in decisionOptions" :key="option" :value="option">{{ option }}</option>
        </select></label>
        <template v-if="decisionForm.type === 'CONDITIONAL_ACCEPT'">
          <label>允许动作<input v-model="decisionForm.allowedActions" required :disabled="!canApprove || busy"></label>
          <label>禁止动作<input v-model="decisionForm.prohibitedActions" required :disabled="!canApprove || busy"></label>
          <label>有效期<input v-model="decisionForm.validUntil" type="datetime-local" required :disabled="!canApprove || busy"></label>
          <label>技术影响<input v-model="decisionForm.technicalImpact" required :disabled="!canApprove || busy"></label>
        </template>
        <label>决定证据引用<input v-model="decisionForm.evidenceRef" required :disabled="!canApprove || busy"></label>
        <label>决定证据 SHA-256<input v-model="decisionForm.evidenceHash" minlength="64" maxlength="64" required :disabled="!canApprove || busy"></label>
        <label>决定理由<textarea v-model="decisionForm.rationale" required :disabled="!canApprove || busy" /></label>
        <p>矩阵：{{ exceptionMatrixVersion }} · 批准人不能是异常发起人</p>
        <a-button type="primary" html-type="submit" :loading="busy" :disabled="!canApprove">提交受控决定</a-button>
      </form>
      <ol><li v-for="decision in current.decisions" :key="decision.decisionId">v{{ decision.version }} · {{ decision.decisionType }} · {{ decision.decidedBy }}</li></ol>
    </template>
    <a-alert v-if="errorCode" type="error" show-icon :message="`异常操作失败：${errorCode}`" description="失败请求不会修改异常事实、对象版本或隔离状态。" />
  </section>
</template>

<style scoped>
.exception-panel { margin: 1rem 0; padding: 1rem; border: 1px solid #faad14; border-radius: .5rem; }
.exception-panel form { display: grid; gap: .65rem; margin-top: 1rem; }
.exception-panel label { display: grid; gap: .25rem; }
</style>

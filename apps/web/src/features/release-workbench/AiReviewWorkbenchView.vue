<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'
import LabAccessNotice from '../lab-workbench/LabAccessNotice.vue'
import LabJsonEditor from '../lab-workbench/LabJsonEditor.vue'
import LabJsonResult from '../lab-workbench/LabJsonResult.vue'
import LabProblemAlert from '../lab-workbench/LabProblemAlert.vue'
import {
  hasArray,
  hasPositiveInteger,
  hasRequiredString,
  hasVersionedReference,
  isJsonRecord,
  parseJsonObject,
  prettyJson,
  type JsonRecord
} from '../lab-workbench/lab-json'
import { useLabOperationState } from '../lab-workbench/lab-operation-state'
import { useLabAccess } from '../lab-workbench/lab-view-state'
import {
  AI_RUNTIME_RULE_SET_VERSION,
  createAiRun,
  getAiReviewQueue,
  getAiRun,
  recordAiDisposition,
  type CreateAiRunRequest,
  type RecordAiDispositionRequest
} from './ai-client'

type AiOperation = 'run' | 'disposition'
type QueueStatus = '' | 'ACCEPTED' | 'QUARANTINED'

const ref1 = { id: 'versioned-ref-id', version: 1 }
const samples: Record<AiOperation, JsonRecord> = {
  run: {
    ruleSetVersion: AI_RUNTIME_RULE_SET_VERSION,
    objectScope: {
      legalEntityId: 'legal-entity-id', laboratoryId: 'laboratory-id',
      customerId: 'customer-id', serviceOrderId: 'service-order-id', productCategory: 'TOYS'
    },
    envelope: {
      model: ref1, gatewayRoute: 'disabled-by-default', promptTemplate: ref1,
      outputSchema: ref1, inputRefs: [ref1]
    },
    validationProfile: ref1, allowedFields: ['sampleNumber', 'quantity'],
    allowedUnits: ['EA', 'mg/kg'], idempotencyKey: 'ai-run-idempotency-key'
  },
  disposition: {
    expectedRunVersion: 1, ruleSetVersion: AI_RUNTIME_RULE_SET_VERSION,
    candidateId: 'candidate-id', kind: 'MODIFY', reason: '按来源证据修正',
    idempotencyKey: 'ai-disposition-idempotency-key', humanValue: '人工确认值'
  }
}

const runAccess = useLabAccess('ai.run')
const reviewAccess = useLabAccess('ai.review')
const operation = ref<AiOperation>('run')
const payloadText = ref(prettyJson(samples.run))
const path = reactive({ runId: '' })
const lookup = reactive<{ runId: string; status: QueueStatus }>({ runId: '', status: '' })
const state = useLabOperationState(runAccess.authenticated, runAccess.accessToken)

watch(operation, value => { payloadText.value = prettyJson(samples[value]) })

const canCurrentWrite = computed(() => operation.value === 'run'
  ? runAccess.canWrite.value
  : reviewAccess.canWrite.value)
const currentCapability = computed(() => operation.value === 'run' ? 'ai.run' : 'ai.review')
const blockedResponse = computed(() => isJsonRecord(state.response.value) &&
  ['QUARANTINED', 'PROVIDER_DISABLED', 'PROVIDER_FAILED', 'UNKNOWN'].includes(
    String(state.response.value.status ?? state.response.value.decision)
  ))

async function submitOperation(): Promise<void> {
  const payload = readPayload()
  if (!payload || !validatePayload(payload)) return
  if (operation.value === 'disposition' && !path.runId.trim()) {
    state.validate('人工处置需要 AI run ID。')
    return
  }

  const result = await state.execute(
    operation.value === 'run' ? 'AI 运行请求已记录' : '人工处置已记录',
    () => operation.value === 'run'
      ? createAiRun(payload as unknown as CreateAiRunRequest, context())
      : recordAiDisposition(path.runId.trim(), payload as unknown as RecordAiDispositionRequest, context()),
    submitOperation
  )
  if (isJsonRecord(result) && typeof result.runId === 'string') {
    path.runId = result.runId
    lookup.runId = result.runId
  }
}

async function loadRun(): Promise<void> {
  if (!lookup.runId.trim()) { state.validate('请输入 AI run ID。'); return }
  const result = await state.execute('AI 运行详情', () => getAiRun(lookup.runId.trim(), context()), loadRun)
  if (result) path.runId = result.runId
}

async function loadQueue(): Promise<void> {
  if (!reviewAccess.canWrite.value) {
    state.validate('读取 AI 复核队列需要 ai.review 能力。')
    return
  }
  await state.execute(
    'AI 复核队列',
    () => getAiReviewQueue(lookup.status || undefined, context()),
    loadQueue
  )
}

function readPayload(): JsonRecord | undefined {
  try { return parseJsonObject(payloadText.value) } catch (error) {
    state.validate(error instanceof Error ? error.message : '请求 JSON 无效。')
    return undefined
  }
}

function validatePayload(payload: JsonRecord): boolean {
  if (payload.ruleSetVersion !== AI_RUNTIME_RULE_SET_VERSION) {
    return state.validate(`规则集必须固定为 ${AI_RUNTIME_RULE_SET_VERSION}。`)
  }
  if (operation.value === 'disposition') {
    const kind = payload.kind
    return state.validate(hasPositiveInteger(payload, 'expectedRunVersion') &&
      hasRequiredString(payload, 'candidateId') && typeof kind === 'string' &&
      ['ACCEPT', 'MODIFY', 'SPLIT', 'MERGE', 'REJECT'].includes(kind) &&
      hasRequiredString(payload, 'reason') && hasRequiredString(payload, 'idempotencyKey') ? '' :
      '人工处置需要运行精确版本、候选项、处置类型、原因和幂等键。')
  }
  const scope = payload.objectScope
  const envelope = payload.envelope
  const validScope = isJsonRecord(scope) && [
    'legalEntityId', 'laboratoryId', 'customerId', 'serviceOrderId', 'productCategory'
  ].every(key => hasRequiredString(scope, key))
  const validEnvelope = isJsonRecord(envelope) && hasVersionedReference(envelope.model) &&
    hasRequiredString(envelope, 'gatewayRoute') && hasVersionedReference(envelope.promptTemplate) &&
    hasVersionedReference(envelope.outputSchema) && hasArray(envelope, 'inputRefs')
  return state.validate(validScope && validEnvelope && hasVersionedReference(payload.validationProfile) &&
    hasArray(payload, 'allowedFields') && hasArray(payload, 'allowedUnits') &&
    hasRequiredString(payload, 'idempotencyKey') ? '' :
    'AI 运行需要完整对象范围、精确模型/模板/Schema/输入引用、验证配置、白名单和幂等键。')
}

function context() { return { accessToken: runAccess.accessToken.value } }
</script>

<template>
  <main class="lab-workbench-page">
    <header class="lab-workbench-heading">
      <p class="eyebrow">RELEASE WORKBENCH / AI RUNTIME</p>
      <h1>AI 提取与人工复核</h1>
      <p>提交可选 AI 运行、查看隔离结果并记录人工处置。AI 提供商默认关闭，人工核心流程不依赖 AI。</p>
    </header>
    <LabAccessNotice
      :status="runAccess.authStatus.value"
      :can-write="canCurrentWrite"
      :capability="currentCapability"
    />

    <template v-if="runAccess.authenticated.value">
      <form class="lab-panel" @submit.prevent="submitOperation">
        <h2>运行与处置</h2>
        <div class="lab-grid">
          <label>操作
            <select v-model="operation" :disabled="state.busy.value">
              <option value="run">提交 AI 运行</option>
              <option value="disposition">记录人工处置</option>
            </select>
          </label>
          <label v-if="operation === 'disposition'">AI run ID
            <input v-model="path.runId" required :disabled="!canCurrentWrite || state.busy.value">
          </label>
        </div>
        <p class="lab-operation-note">原始 AI 输出和人工值分别保存；页面不会把候选项直接提升为业务事实。</p>
        <LabJsonEditor v-model="payloadText" label="请求 JSON" :disabled="!canCurrentWrite || state.busy.value" />
        <div class="lab-actions">
          <button type="submit" :disabled="!canCurrentWrite || state.busy.value">
            {{ state.busy.value ? '处理中...' : '提交操作' }}
          </button>
        </div>
      </form>

      <form class="lab-panel" @submit.prevent="loadRun">
        <h2>运行详情与复核队列</h2>
        <div class="lab-grid">
          <label>AI run ID<input v-model="lookup.runId" :disabled="state.busy.value"></label>
          <label>队列状态
            <select v-model="lookup.status" :disabled="!reviewAccess.canWrite.value || state.busy.value">
              <option value="">全部</option>
              <option value="QUARANTINED">QUARANTINED</option>
              <option value="ACCEPTED">ACCEPTED</option>
            </select>
          </label>
        </div>
        <p v-if="state.validationError.value" class="lab-validation" role="alert">{{ state.validationError.value }}</p>
        <div class="lab-actions">
          <button type="submit" :disabled="state.busy.value">加载运行详情</button>
          <button type="button" class="secondary" :disabled="!reviewAccess.canWrite.value || state.busy.value" @click="loadQueue">加载复核队列</button>
        </div>
      </form>

      <p v-if="state.busy.value" class="lab-panel" role="status">正在等待服务端响应...</p>
      <p v-else-if="!state.response.value && !state.error.value" class="lab-panel lab-empty">尚未加载 AI 运行或复核队列。</p>
      <LabProblemAlert v-if="state.error.value" :error="state.error.value" @retry="state.retryLast" />
      <LabJsonResult v-if="state.response.value" :title="state.responseTitle.value" :value="state.response.value" :blocked="blockedResponse" />
    </template>
  </main>
</template>

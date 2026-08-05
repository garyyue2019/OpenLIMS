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
  addBillingAdjustment,
  BILLING_EXPORT_RULE_SET_VERSION,
  BILLING_HANDOFF_RULE_SET_VERSION,
  BILLING_RULE_SET_VERSION,
  createBillingEvidence,
  createBillingExportBatch,
  createBillingHandoff,
  getBillingDifferenceQueue,
  getBillingEvidence,
  getBillingEvidenceStatus,
  getBillingExportBatch,
  getBillingHandoff,
  recordBillingHandoffAttempt,
  type AddBillingAdjustmentRequest,
  type CreateBillingEvidenceRequest,
  type CreateBillingExportBatchRequest,
  type CreateBillingHandoffRequest,
  type RecordBillingHandoffAttemptRequest
} from './billing-client'

type BillingOperation = 'create' | 'adjustment' | 'export' | 'handoff' | 'attempt'
type BillingLookup = 'evidence' | 'status' | 'export' | 'handoff' | 'differences'

const samples: Record<BillingOperation, JsonRecord> = {
  create: {
    ruleSetVersion: BILLING_RULE_SET_VERSION,
    objectScope: {
      legalEntityId: 'legal-entity-id', laboratoryId: 'laboratory-id',
      customerId: 'customer-id', serviceOrderId: 'service-order-id', productCategory: 'TOYS'
    },
    resultGroupId: 'result-group-id', expectedGroupVersion: 1,
    contractBaseline: { id: 'contract-baseline-id', version: 1 },
    chargeDimension: 'ITEM-PB-TEST', billingRuleVersion: 'PRICE-2026Q3', amount: 120.5,
    currency: { id: 'CNY', version: 1 }
  },
  adjustment: {
    ruleSetVersion: BILLING_RULE_SET_VERSION, amount: -20, reason: 'Approved billing correction.'
  },
  export: {
    ruleSetVersion: BILLING_EXPORT_RULE_SET_VERSION, billingEvidenceIds: ['billing-evidence-id'],
    exportSchemaVersion: 'ERP-SCHEMA@1.0.0', idempotencyKey: 'billing-export-idempotency-key'
  },
  handoff: {
    ruleSetVersion: BILLING_HANDOFF_RULE_SET_VERSION, externalSystem: 'ERP', mode: 'MANUAL',
    endpoint: { id: 'erp-endpoint-id', version: 1 }, idempotencyKey: 'billing-handoff-idempotency-key'
  },
  attempt: {
    ruleSetVersion: BILLING_HANDOFF_RULE_SET_VERSION, idempotencyKey: 'handoff-attempt-idempotency-key',
    outcome: 'FAILED', detailCode: 'ERP_UNAVAILABLE'
  }
}

const { authStatus, authenticated, accessToken, canWrite } = useLabAccess('billing.record')
const operation = ref<BillingOperation>('create')
const payloadText = ref(prettyJson(samples.create))
const path = reactive({ billingEvidenceId: '', exportBatchId: '', handoffId: '' })
const lookup = reactive<{
  kind: BillingLookup
  billingEvidenceId: string
  exportBatchId: string
  handoffId: string
  externalSystem: '' | 'ERP' | 'INVOICE'
}>({ kind: 'evidence', billingEvidenceId: '', exportBatchId: '', handoffId: '', externalSystem: '' })
const state = useLabOperationState(authenticated, accessToken)

watch(operation, value => { payloadText.value = prettyJson(samples[value]) })

const blockedResponse = computed(() => {
  if (!isJsonRecord(state.response.value)) return false
  return ['BLOCKED', 'UNKNOWN', 'FAILED', 'DIFFERENT'].includes(
    String(state.response.value.decision ?? state.response.value.outcome ?? state.response.value.state)
  )
})

async function submitOperation(): Promise<void> {
  const payload = readPayload()
  if (!payload || !validatePayload(payload) || !validatePath()) return

  const result = await state.execute('Billing 写入已完成', async () => {
    if (operation.value === 'create') {
      return createBillingEvidence(payload as unknown as CreateBillingEvidenceRequest, context())
    }
    if (operation.value === 'adjustment') {
      return addBillingAdjustment(
        path.billingEvidenceId.trim(), payload as unknown as AddBillingAdjustmentRequest, context()
      )
    }
    if (operation.value === 'export') {
      return createBillingExportBatch(payload as unknown as CreateBillingExportBatchRequest, context())
    }
    if (operation.value === 'handoff') {
      return createBillingHandoff(
        path.exportBatchId.trim(), payload as unknown as CreateBillingHandoffRequest, context()
      )
    }
    return recordBillingHandoffAttempt(
      path.handoffId.trim(), payload as unknown as RecordBillingHandoffAttemptRequest, context()
    )
  }, submitOperation)

  if (isJsonRecord(result)) {
    if (typeof result.billingEvidenceId === 'string') {
      path.billingEvidenceId = result.billingEvidenceId
      lookup.billingEvidenceId = result.billingEvidenceId
    }
    if (typeof result.exportBatchId === 'string') {
      path.exportBatchId = result.exportBatchId
      lookup.exportBatchId = result.exportBatchId
    }
    if (typeof result.handoffId === 'string') {
      path.handoffId = result.handoffId
      lookup.handoffId = result.handoffId
    }
  }
}

async function loadObject(): Promise<void> {
  if (lookup.kind === 'differences') {
    await state.execute(
      'Billing 差异队列',
      () => getBillingDifferenceQueue(lookup.externalSystem || undefined, context()),
      loadObject
    )
    return
  }
  if (lookup.kind === 'export') {
    if (!lookup.exportBatchId.trim()) { state.validate('请输入 export batch ID。'); return }
    await state.execute(
      'Billing 导出批次', () => getBillingExportBatch(lookup.exportBatchId.trim(), context()), loadObject
    )
    return
  }
  if (lookup.kind === 'handoff') {
    if (!lookup.handoffId.trim()) { state.validate('请输入 handoff ID。'); return }
    await state.execute(
      'Billing 外部交接', () => getBillingHandoff(lookup.handoffId.trim(), context()), loadObject
    )
    return
  }
  if (!lookup.billingEvidenceId.trim()) { state.validate('请输入 billing evidence ID。'); return }
  if (lookup.kind === 'status') {
    await state.execute(
      'Billing 资格状态',
      () => getBillingEvidenceStatus(lookup.billingEvidenceId.trim(), context()),
      loadObject
    )
    return
  }
  const result = await state.execute(
    'Billing 证据详情',
    () => getBillingEvidence(lookup.billingEvidenceId.trim(), context()),
    loadObject
  )
  if (result) path.billingEvidenceId = result.billingEvidenceId
}

function readPayload(): JsonRecord | undefined {
  try { return parseJsonObject(payloadText.value) } catch (error) {
    state.validate(error instanceof Error ? error.message : '请求 JSON 无效。')
    return undefined
  }
}

function validatePath(): boolean {
  if (operation.value === 'adjustment' && !path.billingEvidenceId.trim()) {
    return state.validate('追加调整需要 billing evidence ID。')
  }
  if (operation.value === 'handoff' && !path.exportBatchId.trim()) {
    return state.validate('创建外部交接需要 export batch ID。')
  }
  if (operation.value === 'attempt' && !path.handoffId.trim()) {
    return state.validate('记录交接尝试需要 handoff ID。')
  }
  return true
}

function validatePayload(payload: JsonRecord): boolean {
  const expectedRuleSet = operation.value === 'export'
    ? BILLING_EXPORT_RULE_SET_VERSION
    : operation.value === 'handoff' || operation.value === 'attempt'
      ? BILLING_HANDOFF_RULE_SET_VERSION
      : BILLING_RULE_SET_VERSION
  if (payload.ruleSetVersion !== expectedRuleSet) {
    return state.validate(`规则集必须固定为 ${expectedRuleSet}。`)
  }
  if (operation.value === 'adjustment') {
    const amount = payload.amount
    return state.validate(typeof amount === 'number' && Number.isFinite(amount) && amount !== 0 &&
      hasRequiredString(payload, 'reason') ? '' : '调整金额必须为非零有限数值，并填写非空原因。')
  }
  if (operation.value === 'export') {
    return state.validate(hasArray(payload, 'billingEvidenceIds') &&
      hasRequiredString(payload, 'exportSchemaVersion') && hasRequiredString(payload, 'idempotencyKey') ? '' :
      '导出批次需要账单证据 ID、导出 Schema 版本和幂等键。')
  }
  if (operation.value === 'handoff') {
    const externalSystem = payload.externalSystem
    const mode = payload.mode
    return state.validate((externalSystem === 'ERP' || externalSystem === 'INVOICE') &&
      (mode === 'AUTOMATED' || mode === 'MANUAL') && hasVersionedReference(payload.endpoint) &&
      hasRequiredString(payload, 'idempotencyKey') ? '' :
      '外部交接需要 ERP/INVOICE、模式、版本化端点和幂等键。')
  }
  if (operation.value === 'attempt') {
    const outcome = payload.outcome
    const validOutcome = typeof outcome === 'string' &&
      ['SUCCEEDED', 'FAILED', 'UNKNOWN', 'DIFFERENT'].includes(outcome)
    return state.validate(hasRequiredString(payload, 'idempotencyKey') && validOutcome &&
      (outcome !== 'SUCCEEDED' || hasRequiredString(payload, 'externalReference')) ? '' :
      '交接尝试需要幂等键和结果；SUCCEEDED 必须包含外部引用。')
  }

  const scope = payload.objectScope
  const amount = payload.amount
  const zeroReason = payload.zeroAmountReason
  const validScope = isJsonRecord(scope) && [
    'legalEntityId', 'laboratoryId', 'customerId', 'serviceOrderId', 'productCategory'
  ].every(key => hasRequiredString(scope, key))
  const validAmount = typeof amount === 'number' && Number.isFinite(amount) &&
    (amount === 0
      ? typeof zeroReason === 'string' && zeroReason.trim().length > 0
      : zeroReason === undefined || (typeof zeroReason === 'string' && !zeroReason.trim()))
  const valid = validScope && hasRequiredString(payload, 'resultGroupId') &&
    hasPositiveInteger(payload, 'expectedGroupVersion') && hasVersionedReference(payload.contractBaseline) &&
    hasRequiredString(payload, 'chargeDimension') && hasRequiredString(payload, 'billingRuleVersion') &&
    validAmount && hasVersionedReference(payload.currency)
  return state.validate(valid ? '' :
    '创建证据需要完整对象范围、结果组精确版本、合同/币种版本、收费规则和有效金额；零金额必须且只能填写原因。')
}

function context() { return { accessToken: accessToken.value } }
</script>

<template>
  <main class="lab-workbench-page">
    <header class="lab-workbench-heading">
      <p class="eyebrow">BUSINESS WORKBENCH / BILLING</p>
      <h1>计费证据、导出与外部交接</h1>
      <p>固定结果、合同和收费规则版本，创建不可变计费证据，并跟踪 ERP/开票交接与差异。</p>
    </header>
    <LabAccessNotice :status="authStatus" :can-write="canWrite" capability="billing.record" />

    <template v-if="authenticated">
      <form class="lab-panel" @submit.prevent="submitOperation">
        <h2>执行 Billing 写入</h2>
        <div class="lab-grid">
          <label>操作
            <select v-model="operation" :disabled="!canWrite || state.busy.value">
              <option value="create">创建计费证据</option>
              <option value="adjustment">追加正负调整</option>
              <option value="export">创建导出批次</option>
              <option value="handoff">创建 ERP/开票交接</option>
              <option value="attempt">记录交接尝试</option>
            </select>
          </label>
          <label v-if="operation === 'adjustment'">Billing evidence ID
            <input v-model="path.billingEvidenceId" required :disabled="!canWrite || state.busy.value">
          </label>
          <label v-if="operation === 'handoff'">Export batch ID
            <input v-model="path.exportBatchId" required :disabled="!canWrite || state.busy.value">
          </label>
          <label v-if="operation === 'attempt'">Handoff ID
            <input v-model="path.handoffId" required :disabled="!canWrite || state.busy.value">
          </label>
        </div>
        <p class="lab-operation-note">页面不会推导净额或伪造 ERP、税票成功；外部成功必须由服务端接受外部引用。</p>
        <LabJsonEditor v-model="payloadText" label="请求 JSON" :disabled="!canWrite || state.busy.value" />
        <div class="lab-actions">
          <button type="submit" :disabled="!canWrite || state.busy.value">
            {{ state.busy.value ? '处理中...' : '提交写入' }}
          </button>
        </div>
      </form>

      <form class="lab-panel" @submit.prevent="loadObject">
        <h2>读取证据、导出、交接和差异</h2>
        <div class="lab-grid">
          <label>读取类型
            <select v-model="lookup.kind" :disabled="state.busy.value">
              <option value="evidence">计费证据</option>
              <option value="status">计费资格状态</option>
              <option value="export">导出批次</option>
              <option value="handoff">外部交接</option>
              <option value="differences">差异队列</option>
            </select>
          </label>
          <label v-if="lookup.kind === 'evidence' || lookup.kind === 'status'">Billing evidence ID
            <input v-model="lookup.billingEvidenceId" required :disabled="state.busy.value">
          </label>
          <label v-if="lookup.kind === 'export'">Export batch ID
            <input v-model="lookup.exportBatchId" required :disabled="state.busy.value">
          </label>
          <label v-if="lookup.kind === 'handoff'">Handoff ID
            <input v-model="lookup.handoffId" required :disabled="state.busy.value">
          </label>
          <label v-if="lookup.kind === 'differences'">外部系统筛选
            <select v-model="lookup.externalSystem" :disabled="state.busy.value">
              <option value="">全部</option>
              <option value="ERP">ERP</option>
              <option value="INVOICE">INVOICE</option>
            </select>
          </label>
        </div>
        <p v-if="state.validationError.value" class="lab-validation" role="alert">{{ state.validationError.value }}</p>
        <div class="lab-actions"><button type="submit" :disabled="state.busy.value">加载服务端事实</button></div>
      </form>

      <p v-if="state.busy.value" class="lab-panel" role="status">正在等待服务端响应...</p>
      <p v-else-if="!state.response.value && !state.error.value" class="lab-panel lab-empty">尚未加载计费或交接数据。</p>
      <LabProblemAlert v-if="state.error.value" :error="state.error.value" @retry="state.retryLast" />
      <LabJsonResult v-if="state.response.value" :title="state.responseTitle.value" :value="state.response.value" :blocked="blockedResponse" />
    </template>
  </main>
</template>

<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'
import LabAccessNotice from '../lab-workbench/LabAccessNotice.vue'
import LabJsonEditor from '../lab-workbench/LabJsonEditor.vue'
import LabJsonResult from '../lab-workbench/LabJsonResult.vue'
import LabProblemAlert from '../lab-workbench/LabProblemAlert.vue'
import {
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
  BILLING_RULE_SET_VERSION,
  createBillingEvidence,
  getBillingEvidence,
  getBillingEvidenceStatus,
  type AddBillingAdjustmentRequest,
  type CreateBillingEvidenceRequest
} from './billing-client'

type BillingOperation = 'create' | 'adjustment'

const samples: Record<BillingOperation, JsonRecord> = {
  create: {
    ruleSetVersion: BILLING_RULE_SET_VERSION,
    objectScope: {
      legalEntityId: 'legal-entity-id',
      laboratoryId: 'laboratory-id',
      customerId: 'customer-id',
      serviceOrderId: 'service-order-id',
      productCategory: 'TOYS'
    },
    resultGroupId: 'result-group-id',
    expectedGroupVersion: 1,
    contractBaseline: { id: 'contract-baseline-id', version: 1 },
    chargeDimension: 'ITEM-PB-TEST',
    billingRuleVersion: 'PRICE-2026Q3',
    amount: 120.5,
    currency: { id: 'CNY', version: 1 }
  },
  adjustment: {
    ruleSetVersion: BILLING_RULE_SET_VERSION,
    amount: -20,
    reason: 'Approved billing correction.'
  }
}

const { authStatus, authenticated, accessToken, canWrite } = useLabAccess('billing.record')
const operation = ref<BillingOperation>('create')
const payloadText = ref(prettyJson(samples.create))
const path = reactive({ billingEvidenceId: '' })
const lookup = reactive({ billingEvidenceId: '' })
const state = useLabOperationState(authenticated, accessToken)

watch(operation, value => { payloadText.value = prettyJson(samples[value]) })

const blockedResponse = computed(() => {
  if (!isJsonRecord(state.response.value)) return false
  return state.response.value.decision === 'BLOCKED' || state.response.value.decision === 'UNKNOWN'
})

async function submitOperation(): Promise<void> {
  const payload = readPayload()
  if (!payload || !validatePayload(payload)) return
  if (operation.value === 'adjustment' && !path.billingEvidenceId.trim()) {
    state.validate('追加调整需要 billing evidence ID。')
    return
  }

  const result = await state.execute('Billing 写操作已完成', async () => {
    if (operation.value === 'create') {
      return createBillingEvidence(
        payload as unknown as CreateBillingEvidenceRequest,
        context()
      )
    }
    return addBillingAdjustment(
      path.billingEvidenceId.trim(),
      payload as unknown as AddBillingAdjustmentRequest,
      context()
    )
  }, submitOperation)

  if (result?.billingEvidenceId) {
    path.billingEvidenceId = result.billingEvidenceId
    lookup.billingEvidenceId = result.billingEvidenceId
  }
}

async function loadEvidence(): Promise<void> {
  if (!validLookupId()) return
  const result = await state.execute(
    'Billing 证据详情',
    () => getBillingEvidence(lookup.billingEvidenceId.trim(), context()),
    loadEvidence
  )
  if (result) path.billingEvidenceId = result.billingEvidenceId
}

async function loadStatus(): Promise<void> {
  if (!validLookupId()) return
  await state.execute(
    'Billing 资格状态',
    () => getBillingEvidenceStatus(lookup.billingEvidenceId.trim(), context()),
    loadStatus
  )
}

function readPayload(): JsonRecord | undefined {
  try {
    return parseJsonObject(payloadText.value)
  } catch (error) {
    state.validate(error instanceof Error ? error.message : '请求 JSON 无效。')
    return undefined
  }
}

function validatePayload(payload: JsonRecord): boolean {
  if (payload.ruleSetVersion !== BILLING_RULE_SET_VERSION) {
    return state.validate(`规则集必须固定为 ${BILLING_RULE_SET_VERSION}。`)
  }

  if (operation.value === 'adjustment') {
    const amount = payload.amount
    const valid = typeof amount === 'number' && Number.isFinite(amount) && amount !== 0 &&
      hasRequiredString(payload, 'reason')
    return state.validate(valid ? '' : '调整金额必须为非零有限数值，并填写非空原因。')
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
    hasPositiveInteger(payload, 'expectedGroupVersion') &&
    hasVersionedReference(payload.contractBaseline) &&
    hasRequiredString(payload, 'chargeDimension') &&
    hasRequiredString(payload, 'billingRuleVersion') &&
    validAmount && hasVersionedReference(payload.currency)
  return state.validate(valid ? '' :
    '创建证据需要完整对象范围、结果组精确版本、合同/币种版本、收费规则和有效金额；零金额必须且仅能填写原因。')
}

function validLookupId(): boolean {
  if (lookup.billingEvidenceId.trim()) return true
  state.validate('请输入 billing evidence ID。')
  return false
}

function context() {
  return { accessToken: accessToken.value }
}
</script>

<template>
  <main class="lab-workbench-page">
    <header class="lab-workbench-heading">
      <p class="eyebrow">BUSINESS WORKBENCH · BILLING · {{ BILLING_RULE_SET_VERSION }}</p>
      <h1>计费证据与调整</h1>
      <p>固定结果采用、合同基线、收费维度与币种版本，创建唯一计费证据并追加不可变调整。</p>
    </header>

    <LabAccessNotice :status="authStatus" :can-write="canWrite" capability="billing.record" />

    <template v-if="authenticated">
      <form class="lab-panel" @submit.prevent="submitOperation">
        <h2>执行 Billing 写操作</h2>
        <div class="lab-grid">
          <label>操作
            <select v-model="operation" :disabled="!canWrite || state.busy.value">
              <option value="create">创建计费证据</option>
              <option value="adjustment">追加正负调整</option>
            </select>
          </label>
          <label v-if="operation === 'adjustment'">Billing evidence ID
            <input v-model="path.billingEvidenceId" required :disabled="!canWrite || state.busy.value">
          </label>
        </div>
        <p class="lab-operation-note">浏览器不推导计费资格、最新版本或净额；服务端响应是唯一业务事实。</p>
        <LabJsonEditor v-model="payloadText" label="请求 JSON" :disabled="!canWrite || state.busy.value" />
        <div class="lab-actions">
          <button type="submit" :disabled="!canWrite || state.busy.value">提交写操作</button>
        </div>
      </form>

      <form class="lab-panel" @submit.prevent="loadEvidence">
        <h2>证据详情与资格状态</h2>
        <div class="lab-grid">
          <label>Billing evidence ID
            <input v-model="lookup.billingEvidenceId" required :disabled="state.busy.value">
          </label>
        </div>
        <p v-if="state.validationError.value" class="lab-validation" role="alert">{{ state.validationError.value }}</p>
        <div class="lab-actions">
          <button type="submit" :disabled="state.busy.value">查询证据</button>
          <button type="button" class="secondary" :disabled="state.busy.value" @click="loadStatus">查询服务端状态</button>
        </div>
      </form>

      <LabProblemAlert v-if="state.error.value" :error="state.error.value" @retry="state.retryLast" />
      <LabJsonResult
        v-if="state.response.value"
        :title="state.responseTitle.value"
        :value="state.response.value"
        :blocked="blockedResponse"
      />
    </template>
  </main>
</template>

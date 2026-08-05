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
import { nonNegativeInteger, positiveInteger, useLabAccess } from '../lab-workbench/lab-view-state'
import {
  createCatalogRecord,
  createInquiry,
  createQuoteVersion,
  getCatalogRecord,
  getInquiry,
  recordCapabilityReview,
  recordCommercialChangeImpact,
  resolveInquiryGap,
  reviseCatalogRecord,
  type CapabilityReviewInput,
  type CreateInquiryRequest,
  type RecordChangeImpactRequest,
  type ResolveInquiryGapRequest,
  type SubmitCatalogRecordRequest,
  type SubmitQuoteVersionRequest
} from './commercial-client'

type CommercialOperation = 'catalog-create' | 'catalog-revise' | 'inquiry' | 'gap' |
  'capability' | 'quote' | 'impact'
type CommercialLookup = 'inquiry' | 'catalog'

const ref1 = { id: 'versioned-ref-id', version: 1 }
const objectScope = {
  legalEntityId: 'legal-entity-id', laboratoryId: 'laboratory-id',
  customerId: 'customer-id', serviceOrderId: 'service-order-id', productCategory: 'TOYS'
}
const samples: Record<CommercialOperation, JsonRecord> = {
  'catalog-create': {
    expectedCurrentVersion: 0, kind: 'METHOD', code: 'METHOD-001', displayName: '检测方法',
    validFrom: '2026-08-05T00:00:00Z', state: 'ACTIVE', attributes: { matrix: 'TOYS' },
    references: [ref1], objectScope
  },
  'catalog-revise': {
    expectedCurrentVersion: 1, kind: 'METHOD', code: 'METHOD-001', displayName: '检测方法（修订）',
    validFrom: '2026-08-05T00:00:00Z', state: 'ACTIVE', attributes: { matrix: 'TOYS' },
    references: [ref1], objectScope
  },
  inquiry: {
    details: {
      customerName: '客户名称', productCategory: 'TOYS', quantity: 2, quantityUnit: 'EA',
      testPurpose: '合规检测', expectedTurnaroundDays: 5, sourceDocuments: [ref1]
    },
    objectScope
  },
  gap: { expectedCurrentVersion: 1, value: '2 EA' },
  capability: {
    expectedCurrentVersion: 2, methodCapabilityConfirmed: true, accreditationConfirmed: true,
    personnelAndEquipmentConfirmed: true, sampleQuantityConfirmed: true,
    turnaroundConfirmed: true, confidentialityConfirmed: true, evidence: [ref1], notes: '能力复核完成'
  },
  quote: {
    expectedInquiryVersion: 3, expectedQuoteVersion: 0, scopeMatrix: ref1,
    currency: { id: 'CNY', version: 1 }, contractReference: ref1,
    promisedTurnaroundDays: 5, exclusions: ['物流费用'],
    lines: [{ lineCode: 'LINE-1', description: '检测服务', quantity: 1, unitPrice: 100 }]
  },
  impact: { expectedInquiryVersion: 4, changeKind: 'SCOPE', reason: '客户调整检测范围' }
}

const { authStatus, authenticated, accessToken, canWrite } = useLabAccess('commercial:write')
const operation = ref<CommercialOperation>('inquiry')
const payloadText = ref(prettyJson(samples.inquiry))
const path = reactive({ recordId: '', inquiryId: '', gapId: '' })
const lookup = reactive<{
  kind: CommercialLookup
  recordId: string
  recordVersion: number
  inquiryId: string
}>({ kind: 'inquiry', recordId: '', recordVersion: 1, inquiryId: '' })
const state = useLabOperationState(authenticated, accessToken)

watch(operation, value => { payloadText.value = prettyJson(samples[value]) })

const blockedResponse = computed(() => isJsonRecord(state.response.value) &&
  ['BLOCKED', 'UNKNOWN'].includes(String(state.response.value.decision ?? state.response.value.state)))

async function submitOperation(): Promise<void> {
  const payload = readPayload()
  if (!payload || !validatePayload(payload) || !validatePath()) return

  const result = await state.execute('商业受理写入已完成', async () => {
    if (operation.value === 'catalog-create') {
      return createCatalogRecord(payload as unknown as SubmitCatalogRecordRequest, context())
    }
    if (operation.value === 'catalog-revise') {
      return reviseCatalogRecord(path.recordId.trim(), payload as unknown as SubmitCatalogRecordRequest, context())
    }
    if (operation.value === 'inquiry') {
      return createInquiry(payload as unknown as CreateInquiryRequest, context())
    }
    if (operation.value === 'gap') {
      return resolveInquiryGap(
        path.inquiryId.trim(), path.gapId.trim(), payload as unknown as ResolveInquiryGapRequest, context()
      )
    }
    if (operation.value === 'capability') {
      return recordCapabilityReview(path.inquiryId.trim(), payload as unknown as CapabilityReviewInput, context())
    }
    if (operation.value === 'quote') {
      return createQuoteVersion(path.inquiryId.trim(), payload as unknown as SubmitQuoteVersionRequest, context())
    }
    return recordCommercialChangeImpact(
      path.inquiryId.trim(), payload as unknown as RecordChangeImpactRequest, context()
    )
  }, submitOperation)

  if (result && 'recordId' in result) {
    path.recordId = result.recordId
    lookup.recordId = result.recordId
    lookup.recordVersion = result.version
  }
  if (result && 'inquiryId' in result) {
    path.inquiryId = result.inquiryId
    lookup.inquiryId = result.inquiryId
  }
}

async function loadObject(): Promise<void> {
  if (lookup.kind === 'inquiry') {
    if (!lookup.inquiryId.trim()) { state.validate('请输入 inquiry ID。'); return }
    await state.execute(
      '询价详情', () => getInquiry(lookup.inquiryId.trim(), context()), loadObject
    )
    return
  }
  if (!lookup.recordId.trim() || !positiveInteger(lookup.recordVersion)) {
    state.validate('目录记录查询需要 record ID 和正整数版本。')
    return
  }
  await state.execute(
    '目录记录详情',
    () => getCatalogRecord(lookup.recordId.trim(), lookup.recordVersion, context()),
    loadObject
  )
}

function readPayload(): JsonRecord | undefined {
  try { return parseJsonObject(payloadText.value) } catch (error) {
    state.validate(error instanceof Error ? error.message : '请求 JSON 无效。')
    return undefined
  }
}

function validatePath(): boolean {
  if (operation.value === 'catalog-revise' && !path.recordId.trim()) {
    return state.validate('修订目录记录需要 record ID。')
  }
  if (['gap', 'capability', 'quote', 'impact'].includes(operation.value) && !path.inquiryId.trim()) {
    return state.validate('该操作需要 inquiry ID。')
  }
  if (operation.value === 'gap' && !path.gapId.trim()) {
    return state.validate('解决信息缺口需要 gap ID。')
  }
  return true
}

function validatePayload(payload: JsonRecord): boolean {
  if (operation.value === 'catalog-create' || operation.value === 'catalog-revise') {
    const scope = payload.objectScope
    const validScope = isJsonRecord(scope) && [
      'legalEntityId', 'laboratoryId', 'customerId', 'serviceOrderId', 'productCategory'
    ].every(key => hasRequiredString(scope, key))
    const currentVersion = payload.expectedCurrentVersion
    const validVersion = typeof currentVersion === 'number' && nonNegativeInteger(currentVersion) &&
      (operation.value === 'catalog-create' || currentVersion > 0)
    return state.validate(validScope && validVersion && hasRequiredString(payload, 'kind') &&
      hasRequiredString(payload, 'code') && hasRequiredString(payload, 'displayName') &&
      hasRequiredString(payload, 'validFrom') && hasRequiredString(payload, 'state') &&
      isJsonRecord(payload.attributes) && hasArray(payload, 'references') ? '' :
      '目录记录需要版本、类型、编码、名称、有效期、状态、属性、引用和完整对象范围。')
  }
  if (operation.value === 'inquiry') {
    const details = payload.details
    const scope = payload.objectScope
    const validScope = isJsonRecord(scope) && [
      'legalEntityId', 'laboratoryId', 'customerId', 'serviceOrderId', 'productCategory'
    ].every(key => hasRequiredString(scope, key))
    return state.validate(isJsonRecord(details) && hasArray(details, 'sourceDocuments') && validScope ? '' :
      '询价需要显式详情、来源文档和完整对象范围；缺失业务字段由服务端生成缺口。')
  }
  if (operation.value === 'gap') {
    return state.validate(hasPositiveInteger(payload, 'expectedCurrentVersion') && hasRequiredString(payload, 'value') ? '' :
      '缺口解决需要精确当前版本和非空值。')
  }
  if (operation.value === 'capability') {
    const flags = [
      'methodCapabilityConfirmed', 'accreditationConfirmed', 'personnelAndEquipmentConfirmed',
      'sampleQuantityConfirmed', 'turnaroundConfirmed', 'confidentialityConfirmed'
    ]
    return state.validate(hasPositiveInteger(payload, 'expectedCurrentVersion') &&
      flags.every(key => typeof payload[key] === 'boolean') && hasArray(payload, 'evidence') &&
      typeof payload.notes === 'string' ? '' : '能力复核需要精确版本、六项明确判断、证据和备注。')
  }
  if (operation.value === 'quote') {
    const quoteVersion = payload.expectedQuoteVersion
    return state.validate(hasPositiveInteger(payload, 'expectedInquiryVersion') &&
      typeof quoteVersion === 'number' && nonNegativeInteger(quoteVersion) &&
      hasVersionedReference(payload.scopeMatrix) && hasVersionedReference(payload.currency) &&
      hasVersionedReference(payload.contractReference) &&
      hasPositiveInteger(payload, 'promisedTurnaroundDays') && hasArray(payload, 'exclusions') &&
      hasArray(payload, 'lines') ? '' : '报价需要询价版本、报价版本、范围/币种/合同引用、交付天数和明细。')
  }
  return state.validate(hasPositiveInteger(payload, 'expectedInquiryVersion') &&
    hasRequiredString(payload, 'changeKind') && hasRequiredString(payload, 'reason') ? '' :
    '变更影响需要询价精确版本、变更类型和原因。')
}

function context() { return { accessToken: accessToken.value } }
</script>

<template>
  <main class="lab-workbench-page">
    <header class="lab-workbench-heading">
      <p class="eyebrow">RELEASE WORKBENCH / COMMERCIAL</p>
      <h1>商业受理与报价</h1>
      <p>维护版本化目录，受理客户询价，显式处理信息缺口、能力复核、报价版本与变更影响。</p>
    </header>
    <LabAccessNotice :status="authStatus" :can-write="canWrite" capability="commercial:write" />

    <template v-if="authenticated">
      <form class="lab-panel" @submit.prevent="submitOperation">
        <h2>执行商业写入</h2>
        <div class="lab-grid">
          <label>操作
            <select v-model="operation" :disabled="!canWrite || state.busy.value">
              <option value="catalog-create">创建目录记录</option>
              <option value="catalog-revise">修订目录记录</option>
              <option value="inquiry">创建询价</option>
              <option value="gap">解决信息缺口</option>
              <option value="capability">记录能力复核</option>
              <option value="quote">创建报价版本</option>
              <option value="impact">记录变更影响</option>
            </select>
          </label>
          <label v-if="operation === 'catalog-revise'">Record ID
            <input v-model="path.recordId" required :disabled="!canWrite || state.busy.value">
          </label>
          <label v-if="['gap', 'capability', 'quote', 'impact'].includes(operation)">Inquiry ID
            <input v-model="path.inquiryId" required :disabled="!canWrite || state.busy.value">
          </label>
          <label v-if="operation === 'gap'">Gap ID
            <input v-model="path.gapId" required :disabled="!canWrite || state.busy.value">
          </label>
        </div>
        <p class="lab-operation-note">页面只提交显式版本和事实；缺失字段、能力结论与报价状态均由服务端记录。</p>
        <LabJsonEditor v-model="payloadText" label="请求 JSON" :disabled="!canWrite || state.busy.value" />
        <div class="lab-actions">
          <button type="submit" :disabled="!canWrite || state.busy.value">
            {{ state.busy.value ? '处理中...' : '提交写入' }}
          </button>
        </div>
      </form>

      <form class="lab-panel" @submit.prevent="loadObject">
        <h2>读取商业对象</h2>
        <div class="lab-grid">
          <label>对象类型
            <select v-model="lookup.kind" :disabled="state.busy.value">
              <option value="inquiry">询价</option>
              <option value="catalog">目录记录版本</option>
            </select>
          </label>
          <label v-if="lookup.kind === 'inquiry'">Inquiry ID
            <input v-model="lookup.inquiryId" required :disabled="state.busy.value">
          </label>
          <template v-else>
            <label>Record ID<input v-model="lookup.recordId" required :disabled="state.busy.value"></label>
            <label>精确版本<input v-model.number="lookup.recordVersion" type="number" min="1" step="1" required :disabled="state.busy.value"></label>
          </template>
        </div>
        <p v-if="state.validationError.value" class="lab-validation" role="alert">{{ state.validationError.value }}</p>
        <div class="lab-actions"><button type="submit" :disabled="state.busy.value">加载服务端事实</button></div>
      </form>

      <p v-if="state.busy.value" class="lab-panel" role="status">正在等待服务端响应...</p>
      <p v-else-if="!state.response.value && !state.error.value" class="lab-panel lab-empty">尚未加载商业对象。</p>
      <LabProblemAlert v-if="state.error.value" :error="state.error.value" @retry="state.retryLast" />
      <LabJsonResult v-if="state.response.value" :title="state.responseTitle.value" :value="state.response.value" :blocked="blockedResponse" />
    </template>
  </main>
</template>

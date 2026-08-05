<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'
import LabAccessNotice from '../lab-workbench/LabAccessNotice.vue'
import LabJsonEditor from '../lab-workbench/LabJsonEditor.vue'
import LabJsonResult from '../lab-workbench/LabJsonResult.vue'
import LabProblemAlert from '../lab-workbench/LabProblemAlert.vue'
import {
  hasArray,
  hasNonNegativeInteger,
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
  freezeToyAgeGradeDecision,
  getToyProductOverview,
  recordToyAccessibilityAssessment,
  recordToyAgeDeclaration,
  recordToyAgeGradeDecision,
  resolveToyReassessmentTrigger,
  TOY_PRODUCT_RULE_SET_VERSION,
  type FreezeToyAgeGradeDecisionRequest,
  type RecordToyAccessibilityAssessmentRequest,
  type RecordToyAgeDeclarationRequest,
  type RecordToyAgeGradeDecisionRequest,
  type ResolveToyReassessmentTriggerRequest
} from './toy-product-client'

type ProductOperation = 'declaration' | 'decision' | 'freeze' | 'assessment' | 'resolve'
const base = { ruleSetVersion: TOY_PRODUCT_RULE_SET_VERSION, expectedCurrentVersion: 0 }
const scope = { legalEntityId: 'legal-entity-id', laboratoryId: 'laboratory-id' }
const samples: Record<ProductOperation, JsonRecord> = {
  declaration: {
    ...base, objectScope: scope, declaredMinimumAgeMonths: 36,
    intendedUse: 'Indoor play under adult supervision.', declarationSource: 'customer-declaration-ref'
  },
  decision: {
    ...base, objectScope: scope, minimumAgeMonths: 36,
    rationale: 'Age grading based on the pinned standard and product evidence.',
    standardRef: { id: 'toy-standard-id', version: 1 }, approvedBy: 'technical-approver-business-id'
  },
  freeze: { ruleSetVersion: TOY_PRODUCT_RULE_SET_VERSION, expectedCurrentVersion: 1 },
  assessment: {
    ...base, objectScope: scope, stage: 'INITIAL', accessibleParts: ['wheel', 'fastener']
  },
  resolve: {
    ruleSetVersion: TOY_PRODUCT_RULE_SET_VERSION, expectedCurrentVersion: 1,
    resolutionRef: { id: 'reassessment-resolution-id', version: 1 }
  }
}

const { authStatus, authenticated, accessToken, canWrite } = useLabAccess('toy.manage')
const operation = ref<ProductOperation>('declaration')
const payloadText = ref(prettyJson(samples.declaration))
const path = reactive({ productId: '', decisionVersion: 1, triggerId: '' })
const lookup = reactive({ productId: '' })
const state = useLabOperationState(authenticated, accessToken)

watch(operation, value => { payloadText.value = prettyJson(samples[value]) })

const blockedResponse = computed(() => isJsonRecord(state.response.value) &&
  state.response.value.accessibilityStatus === 'REASSESSMENT_PENDING')

async function submitOperation(): Promise<void> {
  const payload = readPayload()
  if (!payload || !validatePayload(payload) || !path.productId.trim()) {
    if (!path.productId.trim()) state.validate('Toy 产品写操作需要 product ID。')
    return
  }
  if (operation.value === 'freeze' && !positiveInteger(path.decisionVersion)) {
    state.validate('冻结需要正整数年龄决定版本。')
    return
  }
  if (operation.value === 'resolve' && !path.triggerId.trim()) {
    state.validate('解决重评触发需要 trigger ID。')
    return
  }

  const productId = path.productId.trim()
  const result = await state.execute('Toy 产品操作已完成', async () => {
    if (operation.value === 'declaration') {
      return recordToyAgeDeclaration(productId, payload as unknown as RecordToyAgeDeclarationRequest, context())
    }
    if (operation.value === 'decision') {
      return recordToyAgeGradeDecision(productId, payload as unknown as RecordToyAgeGradeDecisionRequest, context())
    }
    if (operation.value === 'freeze') {
      return freezeToyAgeGradeDecision(
        productId, path.decisionVersion,
        payload as unknown as FreezeToyAgeGradeDecisionRequest, context()
      )
    }
    if (operation.value === 'assessment') {
      return recordToyAccessibilityAssessment(
        productId, payload as unknown as RecordToyAccessibilityAssessmentRequest, context()
      )
    }
    return resolveToyReassessmentTrigger(
      productId, path.triggerId.trim(),
      payload as unknown as ResolveToyReassessmentTriggerRequest, context()
    )
  }, submitOperation)
  if (result) lookup.productId = result.productId
}

async function loadOverview(): Promise<void> {
  if (!lookup.productId.trim()) {
    state.validate('请输入 product ID。')
    return
  }
  const result = await state.execute(
    'Toy 产品概览',
    () => getToyProductOverview(lookup.productId.trim(), context()),
    loadOverview
  )
  if (result) path.productId = result.productId
}

function readPayload(): JsonRecord | undefined {
  try { return parseJsonObject(payloadText.value) } catch (error) {
    state.validate(error instanceof Error ? error.message : '请求 JSON 无效。')
    return undefined
  }
}

function validatePayload(payload: JsonRecord): boolean {
  if (payload.ruleSetVersion !== TOY_PRODUCT_RULE_SET_VERSION) {
    return state.validate(`规则集必须固定为 ${TOY_PRODUCT_RULE_SET_VERSION}。`)
  }
  const expectedValid = operation.value === 'freeze' || operation.value === 'resolve'
    ? hasPositiveInteger(payload, 'expectedCurrentVersion')
    : hasNonNegativeInteger(payload, 'expectedCurrentVersion')
  if (!expectedValid) return state.validate('expectedCurrentVersion 不符合当前操作的精确版本边界。')
  if (operation.value === 'freeze') return state.validate('')
  if (operation.value === 'resolve') {
    return state.validate(hasVersionedReference(payload.resolutionRef) ? '' : '解决重评需要精确 resolutionRef。')
  }
  const objectScope = payload.objectScope
  const validScope = isJsonRecord(objectScope) && hasRequiredString(objectScope, 'legalEntityId') &&
    hasRequiredString(objectScope, 'laboratoryId')
  if (!validScope) return state.validate('需要完整法人和实验室对象范围。')
  if (operation.value === 'declaration') {
    const valid = hasNonNegativeInteger(payload, 'declaredMinimumAgeMonths') &&
      hasRequiredString(payload, 'intendedUse') && hasRequiredString(payload, 'declarationSource')
    return state.validate(valid ? '' : '年龄声明需要非负月龄、用途和声明来源。')
  }
  if (operation.value === 'decision') {
    const valid = hasNonNegativeInteger(payload, 'minimumAgeMonths') &&
      hasRequiredString(payload, 'rationale') && hasVersionedReference(payload.standardRef) &&
      hasRequiredString(payload, 'approvedBy')
    return state.validate(valid ? '' : '年龄决定需要非负月龄、理由、标准精确版本和业务批准引用。')
  }
  const stage = payload.stage
  const partsValid = hasArray(payload, 'accessibleParts') &&
    (payload.accessibleParts as unknown[]).every(item => typeof item === 'string' && item.trim())
  const abuseValid = stage === 'AFTER_ABUSE'
    ? hasRequiredString(payload, 'abuseEventRef')
    : payload.abuseEventRef === undefined || payload.abuseEventRef === null || payload.abuseEventRef === ''
  const valid = typeof stage === 'string' &&
    ['INITIAL', 'AFTER_NORMAL_USE', 'AFTER_ABUSE'].includes(stage) && partsValid && abuseValid
  return state.validate(valid ? '' : '可及性评估需要批准阶段和非空部件；仅 AFTER_ABUSE 必须携带滥用事件。')
}

function positiveInteger(value: number): boolean { return Number.isInteger(value) && value > 0 }
function context() { return { accessToken: accessToken.value } }
</script>

<template>
  <main class="lab-workbench-page">
    <header class="lab-workbench-heading">
      <p class="eyebrow">TOY WORKBENCH · PRODUCT · {{ TOY_PRODUCT_RULE_SET_VERSION }}</p>
      <h1>玩具年龄分级与可及性</h1>
      <p>分离客户声明和实验室决定，冻结精确决定版本，并按使用阶段记录可及性与范围重评。</p>
    </header>
    <LabAccessNotice :status="authStatus" :can-write="canWrite" capability="toy.manage" />

    <template v-if="authenticated">
      <form class="lab-panel" @submit.prevent="submitOperation">
        <h2>执行产品链操作</h2>
        <div class="lab-grid">
          <label>操作
            <select v-model="operation" :disabled="!canWrite || state.busy.value">
              <option value="declaration">记录客户年龄声明</option>
              <option value="decision">记录实验室年龄决定</option>
              <option value="freeze">冻结年龄决定版本</option>
              <option value="assessment">记录可及性评估</option>
              <option value="resolve">解决范围重评触发</option>
            </select>
          </label>
          <label>Product ID<input v-model="path.productId" required :disabled="!canWrite || state.busy.value"></label>
          <label v-if="operation === 'freeze'">年龄决定版本<input v-model.number="path.decisionVersion" type="number" min="1" step="1" required :disabled="!canWrite || state.busy.value"></label>
          <label v-if="operation === 'resolve'">Trigger ID<input v-model="path.triggerId" required :disabled="!canWrite || state.busy.value"></label>
        </div>
        <p class="lab-operation-note">approvedBy 是批准契约中的业务引用，不是浏览器选择的会话身份；服务器仍执行最终授权。</p>
        <LabJsonEditor v-model="payloadText" label="请求 JSON" :disabled="!canWrite || state.busy.value" />
        <div class="lab-actions"><button type="submit" :disabled="!canWrite || state.busy.value">提交写操作</button></div>
      </form>

      <form class="lab-panel" @submit.prevent="loadOverview">
        <h2>查询产品概览</h2>
        <div class="lab-grid"><label>Product ID<input v-model="lookup.productId" required :disabled="state.busy.value"></label></div>
        <p v-if="state.validationError.value" class="lab-validation" role="alert">{{ state.validationError.value }}</p>
        <div class="lab-actions"><button type="submit" :disabled="state.busy.value">读取概览</button></div>
      </form>

      <LabProblemAlert v-if="state.error.value" :error="state.error.value" @retry="state.retryLast" />
      <LabJsonResult v-if="state.response.value" :title="state.responseTitle.value" :value="state.response.value" :blocked="blockedResponse" />
    </template>
  </main>
</template>

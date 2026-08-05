<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'
import LabAccessNotice from './LabAccessNotice.vue'
import LabJsonEditor from './LabJsonEditor.vue'
import LabJsonResult from './LabJsonResult.vue'
import LabProblemAlert from './LabProblemAlert.vue'
import {
  hasArray,
  hasPositiveInteger,
  hasRequiredString,
  hasSha256,
  hasVersionedReference,
  isJsonRecord,
  parseJsonObject,
  prettyJson,
  type JsonRecord
} from './lab-json'
import { useLabOperationState } from './lab-operation-state'
import { positiveInteger, useLabAccess } from './lab-view-state'
import {
  addResultDerivation,
  addResultObservation,
  adoptResult,
  createResultGroup,
  executeResultCalculation,
  getResultAccreditationEligibility,
  getResultAdoptionStatus,
  getResultGroup,
  recordAdoptionRule,
  recordResultAccreditationAssessment,
  RESULT_ACCREDITATION_RULE_SET_VERSION,
  RESULT_CALCULATION_RULE_SET_VERSION,
  RESULT_RULE_SET_VERSION,
  type AddResultDerivationRequest,
  type AddResultObservationRequest,
  type AdoptResultRequest,
  type CreateResultGroupRequest,
  type ExecuteResultCalculationRequest,
  type RecordAdoptionRuleRequest,
  type RecordResultAccreditationAssessmentRequest
} from './result-client'

type ResultOperation = 'create' | 'observation' | 'derivation' | 'calculation' |
  'rule' | 'adopt' | 'accreditation'

const ref1 = { id: 'versioned-ref-id', version: 1 }
const samples: Record<ResultOperation, JsonRecord> = {
  create: {
    ruleSetVersion: RESULT_RULE_SET_VERSION,
    objectScope: {
      legalEntityId: 'legal-entity-id', laboratoryId: 'laboratory-id',
      customerId: 'customer-id', serviceOrderId: 'service-order-id', productCategory: 'PRODUCT'
    },
    batchId: 'batch-id', expectedBatchVersion: 1, memberId: 'member-id',
    testItem: ref1, scopeLineId: 'scope-line-id'
  },
  observation: {
    expectedCurrentVersion: 1, ruleSetVersion: RESULT_RULE_SET_VERSION,
    kind: 'INITIAL', value: '1.20', unit: 'mg/kg',
    evidence: {
      sourceSystem: 'INSTRUMENT', externalRef: ref1,
      sha256: 'b'.repeat(64), parserVersion: 'parser@1.0.0'
    }
  },
  derivation: {
    expectedCurrentVersion: 2, ruleSetVersion: RESULT_RULE_SET_VERSION,
    aggregationRule: ref1, value: '1.20', unit: 'mg/kg',
    inputs: [{ targetId: 'observation-id', included: true, rationale: 'Included by rule.' }]
  },
  calculation: {
    expectedCurrentVersion: 3, ruleSetVersion: RESULT_CALCULATION_RULE_SET_VERSION,
    inputs: [{ targetId: 'observation-id', coefficient: 1 }],
    rule: {
      calculationRule: ref1, unitConversionRule: ref1, inputUnit: 'mg/kg', outputUnit: 'mg/kg',
      unitMultiplier: 1, unitOffset: 0, dilutionFactor: 1, quantityFactor: 1,
      decimalPlaces: 2, roundingMode: 'HALF_UP', limitOperator: 'BETWEEN',
      limitEvaluationBasis: 'ROUNDED', lowerLimit: 0, upperLimit: 10
    }
  },
  rule: {
    expectedCurrentVersion: 4, ruleSetVersion: RESULT_RULE_SET_VERSION,
    strategy: 'RETEST_REPLACES_ORIGINAL', ruleRef: ref1
  },
  adopt: {
    expectedCurrentVersion: 5, ruleSetVersion: RESULT_RULE_SET_VERSION,
    targetId: 'observation-or-derivation-id'
  },
  accreditation: {
    expectedCurrentVersion: 6, ruleSetVersion: RESULT_ACCREDITATION_RULE_SET_VERSION,
    stage: 'RESULT', targetId: 'observation-or-derivation-id',
    accreditation: ref1, method: ref1, siteId: 'site-id', productOrMatrix: 'matrix',
    parameter: 'lead', rangeUnit: 'mg/kg', rangeLower: 0, rangeUpper: 10,
    validFrom: '2026-01-01T00:00:00Z', validTo: '2027-01-01T00:00:00Z',
    authorizedActorIds: ['analyst-id']
  }
}

const { authStatus, authenticated, accessToken, canWrite } = useLabAccess('result.record')
const operation = ref<ResultOperation>('create')
const payloadText = ref(prettyJson(samples.create))
const path = reactive({ resultGroupId: '' })
const lookup = reactive({ resultGroupId: '', expectedVersion: 1 })
const state = useLabOperationState(authenticated, accessToken)

watch(operation, value => { payloadText.value = prettyJson(samples[value]) })

const blockedResponse = computed(() => isJsonRecord(state.response.value) &&
  (state.response.value.decision === 'BLOCKED' || state.response.value.decision === 'UNKNOWN'))

async function submitOperation(): Promise<void> {
  const payload = readPayload()
  if (!payload || !validatePayload(payload)) return
  if (operation.value !== 'create' && !path.resultGroupId.trim()) {
    state.validate('后续结果操作需要 result group ID。')
    return
  }
  const result = await state.execute('Result 写入已完成', async () => {
    const id = path.resultGroupId.trim()
    if (operation.value === 'create') {
      return createResultGroup(payload as unknown as CreateResultGroupRequest, context())
    }
    if (operation.value === 'observation') {
      return addResultObservation(id, payload as unknown as AddResultObservationRequest, context())
    }
    if (operation.value === 'derivation') {
      return addResultDerivation(id, payload as unknown as AddResultDerivationRequest, context())
    }
    if (operation.value === 'calculation') {
      return executeResultCalculation(id, payload as unknown as ExecuteResultCalculationRequest, context())
    }
    if (operation.value === 'rule') {
      return recordAdoptionRule(id, payload as unknown as RecordAdoptionRuleRequest, context())
    }
    if (operation.value === 'adopt') {
      return adoptResult(id, payload as unknown as AdoptResultRequest, context())
    }
    return recordResultAccreditationAssessment(
      id, payload as unknown as RecordResultAccreditationAssessmentRequest, context()
    )
  }, submitOperation)
  if (isJsonRecord(result)) {
    const id = result.resultGroupId
    const version = result.groupVersion ?? result.version
    if (typeof id === 'string' && typeof version === 'number') setCurrentGroup(id, version)
  }
}

async function loadGroup(): Promise<void> {
  if (!lookup.resultGroupId.trim()) { state.validate('请输入 result group ID。'); return }
  const result = await state.execute(
    'Result 结果组详情',
    () => getResultGroup(lookup.resultGroupId.trim(), context()),
    loadGroup
  )
  if (result) setCurrentGroup(result.resultGroupId, result.version)
}

async function checkAdoptionStatus(): Promise<void> {
  if (!validLookup()) return
  await state.execute(
    'Result 采用状态',
    () => getResultAdoptionStatus(lookup.resultGroupId.trim(), lookup.expectedVersion, context()),
    checkAdoptionStatus
  )
}

async function checkAccreditationEligibility(): Promise<void> {
  if (!validLookup()) return
  await state.execute(
    'Result 认可资格',
    () => getResultAccreditationEligibility(
      lookup.resultGroupId.trim(), lookup.expectedVersion, context()
    ),
    checkAccreditationEligibility
  )
}

function validLookup(): boolean {
  if (lookup.resultGroupId.trim() && positiveInteger(lookup.expectedVersion)) return true
  state.validate('状态查询需要 result group ID 和正整数精确版本。')
  return false
}

function readPayload(): JsonRecord | undefined {
  try { return parseJsonObject(payloadText.value) } catch (error) {
    state.validate(error instanceof Error ? error.message : '请求 JSON 无效。')
    return undefined
  }
}

function validatePayload(payload: JsonRecord): boolean {
  const expectedRuleSet = operation.value === 'calculation'
    ? RESULT_CALCULATION_RULE_SET_VERSION
    : operation.value === 'accreditation'
      ? RESULT_ACCREDITATION_RULE_SET_VERSION
      : RESULT_RULE_SET_VERSION
  if (payload.ruleSetVersion !== expectedRuleSet) {
    return state.validate(`规则集必须固定为 ${expectedRuleSet}。`)
  }
  if (operation.value === 'create') {
    const scope = payload.objectScope
    const validScope = isJsonRecord(scope) && [
      'legalEntityId', 'laboratoryId', 'customerId', 'serviceOrderId', 'productCategory'
    ].every(key => hasRequiredString(scope, key))
    const valid = validScope && hasRequiredString(payload, 'batchId') &&
      hasPositiveInteger(payload, 'expectedBatchVersion') && hasRequiredString(payload, 'memberId') &&
      hasVersionedReference(payload.testItem) && hasRequiredString(payload, 'scopeLineId')
    return state.validate(valid ? '' : '创建结果组需要完整对象范围、Batch 精确版本、成员、测试项和范围行。')
  }
  if (!hasPositiveInteger(payload, 'expectedCurrentVersion')) {
    return state.validate('后续结果操作需要正整数 expectedCurrentVersion。')
  }
  if (operation.value === 'observation') {
    const evidence = payload.evidence
    const validEvidence = isJsonRecord(evidence) && hasRequiredString(evidence, 'sourceSystem') &&
      hasVersionedReference(evidence.externalRef) && hasSha256(evidence, 'sha256') &&
      hasRequiredString(evidence, 'parserVersion')
    return state.validate(hasRequiredString(payload, 'kind') && hasRequiredString(payload, 'value') &&
      hasRequiredString(payload, 'unit') && validEvidence ? '' :
      '观察需要类型、值、单位，以及带精确外部引用、SHA-256 和解析器版本的证据。')
  }
  if (operation.value === 'derivation') {
    return state.validate(hasVersionedReference(payload.aggregationRule) && hasRequiredString(payload, 'value') &&
      hasRequiredString(payload, 'unit') && hasArray(payload, 'inputs') ? '' :
      '推导需要聚合规则精确版本、值、单位和来源输入。')
  }
  if (operation.value === 'calculation') {
    const rule = payload.rule
    const validRule = isJsonRecord(rule) && hasVersionedReference(rule.calculationRule) &&
      hasVersionedReference(rule.unitConversionRule) && hasRequiredString(rule, 'inputUnit') &&
      hasRequiredString(rule, 'outputUnit') && typeof rule.unitMultiplier === 'number' &&
      typeof rule.unitOffset === 'number' && typeof rule.dilutionFactor === 'number' &&
      typeof rule.quantityFactor === 'number' && typeof rule.decimalPlaces === 'number' &&
      hasRequiredString(rule, 'roundingMode') && hasRequiredString(rule, 'limitOperator') &&
      hasRequiredString(rule, 'limitEvaluationBasis')
    return state.validate(hasArray(payload, 'inputs') && validRule ? '' :
      '计算需要输入目标，以及版本化计算/单位规则、换算、舍入和限值配置。')
  }
  if (operation.value === 'rule') {
    const strategy = payload.strategy
    return state.validate((strategy === 'RETEST_REPLACES_ORIGINAL' || strategy === 'TECHNICAL_REVIEW_SELECTS') &&
      hasVersionedReference(payload.ruleRef) ? '' : '采用规则需要批准的策略和规则精确引用。')
  }
  if (operation.value === 'adopt') {
    return state.validate(hasRequiredString(payload, 'targetId') ? '' : '采用请求需要 target ID。')
  }
  const stage = payload.stage
  return state.validate((stage === 'EXECUTION' || stage === 'RESULT') &&
    hasVersionedReference(payload.accreditation) && hasVersionedReference(payload.method) &&
    hasRequiredString(payload, 'siteId') && hasRequiredString(payload, 'productOrMatrix') &&
    hasRequiredString(payload, 'parameter') && hasRequiredString(payload, 'rangeUnit') &&
    typeof payload.rangeLower === 'number' && typeof payload.rangeUpper === 'number' &&
    hasRequiredString(payload, 'validFrom') && hasRequiredString(payload, 'validTo') &&
    hasArray(payload, 'authorizedActorIds') &&
    (stage !== 'RESULT' || hasRequiredString(payload, 'targetId')) ? '' :
    '认可评估需要阶段、精确认可/方法引用、场所、矩阵、参数、范围、有效期和授权人员；结果阶段还需要 target ID。')
}

function setCurrentGroup(id: string, version: number): void {
  path.resultGroupId = id
  lookup.resultGroupId = id
  lookup.expectedVersion = version
}

function context() { return { accessToken: accessToken.value } }
</script>

<template>
  <main class="lab-workbench-page">
    <header class="lab-workbench-heading">
      <p class="eyebrow">LAB WORKBENCH / RESULT</p>
      <h1>结果来源、计算与采用</h1>
      <p>从 Batch 创建结果组，追加观察和推导，执行版本化计算，记录认可评估并采用精确结果版本。</p>
    </header>
    <LabAccessNotice :status="authStatus" :can-write="canWrite" capability="result.record" />

    <template v-if="authenticated">
      <form class="lab-panel" @submit.prevent="submitOperation">
        <h2>执行 Result 操作</h2>
        <div class="lab-grid">
          <label>操作
            <select v-model="operation" :disabled="!canWrite || state.busy.value">
              <option value="create">创建结果组</option>
              <option value="observation">追加观察</option>
              <option value="derivation">追加推导</option>
              <option value="calculation">执行版本化计算</option>
              <option value="rule">记录采用规则</option>
              <option value="adopt">采用结果</option>
              <option value="accreditation">记录认可评估</option>
            </select>
          </label>
          <label v-if="operation !== 'create'">Result group ID
            <input v-model="path.resultGroupId" required :disabled="!canWrite || state.busy.value">
          </label>
        </div>
        <p class="lab-operation-note">页面不选择“最新”规则、不重新计算服务端事实，也不自动采用目标。</p>
        <LabJsonEditor v-model="payloadText" label="请求 JSON" :disabled="!canWrite || state.busy.value" />
        <div class="lab-actions">
          <button type="submit" :disabled="!canWrite || state.busy.value">
            {{ state.busy.value ? '处理中...' : '提交写入' }}
          </button>
        </div>
      </form>

      <form class="lab-panel" @submit.prevent="loadGroup">
        <h2>结果组、采用状态与认可资格</h2>
        <div class="lab-grid">
          <label>Result group ID<input v-model="lookup.resultGroupId" required :disabled="state.busy.value"></label>
          <label>结果组精确版本<input v-model.number="lookup.expectedVersion" type="number" min="1" step="1" required :disabled="state.busy.value"></label>
        </div>
        <p v-if="state.validationError.value" class="lab-validation" role="alert">{{ state.validationError.value }}</p>
        <div class="lab-actions">
          <button type="submit" :disabled="state.busy.value">加载结果组</button>
          <button type="button" class="secondary" :disabled="state.busy.value" @click="checkAdoptionStatus">检查采用状态</button>
          <button type="button" class="secondary" :disabled="state.busy.value" @click="checkAccreditationEligibility">检查认可资格</button>
        </div>
      </form>

      <p v-if="state.busy.value" class="lab-panel" role="status">正在等待服务端响应...</p>
      <p v-else-if="!state.response.value && !state.error.value" class="lab-panel lab-empty">尚未加载结果组或状态。</p>
      <LabProblemAlert v-if="state.error.value" :error="state.error.value" @retry="state.retryLast" />
      <LabJsonResult v-if="state.response.value" :title="state.responseTitle.value" :value="state.response.value" :blocked="blockedResponse" />
    </template>
  </main>
</template>

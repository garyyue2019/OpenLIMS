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
  getResultAdoptionStatus,
  getResultGroup,
  recordAdoptionRule,
  RESULT_RULE_SET_VERSION,
  type AddResultDerivationRequest,
  type AddResultObservationRequest,
  type AdoptResultRequest,
  type CreateResultGroupRequest,
  type RecordAdoptionRuleRequest
} from './result-client'

type ResultOperation = 'create' | 'observation' | 'derivation' | 'rule' | 'adopt'

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
    expectedCurrentVersion: 1,
    ruleSetVersion: RESULT_RULE_SET_VERSION,
    kind: 'INITIAL', value: '1.20', unit: 'mg/kg',
    evidence: {
      sourceSystem: 'INSTRUMENT', externalRef: ref1,
      sha256: 'b'.repeat(64), parserVersion: 'parser@1.0.0'
    }
  },
  derivation: {
    expectedCurrentVersion: 2,
    ruleSetVersion: RESULT_RULE_SET_VERSION,
    aggregationRule: ref1, value: '1.20', unit: 'mg/kg',
    inputs: [{ targetId: 'observation-id', included: true, rationale: 'Included by rule.' }]
  },
  rule: {
    expectedCurrentVersion: 3,
    ruleSetVersion: RESULT_RULE_SET_VERSION,
    strategy: 'RETEST_REPLACES_ORIGINAL', ruleRef: ref1
  },
  adopt: {
    expectedCurrentVersion: 4,
    ruleSetVersion: RESULT_RULE_SET_VERSION,
    targetId: 'observation-or-derivation-id'
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
    state.validate('追加或采用操作需要结果组 ID。')
    return
  }
  const result = await state.execute('Result 写操作已完成', async () => {
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
    if (operation.value === 'rule') {
      return recordAdoptionRule(id, payload as unknown as RecordAdoptionRuleRequest, context())
    }
    return adoptResult(id, payload as unknown as AdoptResultRequest, context())
  }, submitOperation)
  if (result && 'resultGroupId' in result && 'groupVersion' in result) {
    setCurrentGroup(result.resultGroupId, result.groupVersion)
  } else if (result && 'resultGroupId' in result && 'version' in result) {
    setCurrentGroup(result.resultGroupId, result.version)
  }
}

async function loadGroup(): Promise<void> {
  if (!lookup.resultGroupId.trim()) { state.validate('请输入结果组 ID。'); return }
  const result = await state.execute(
    'Result 结果组详情',
    () => getResultGroup(lookup.resultGroupId.trim(), context()),
    loadGroup
  )
  if (result) setCurrentGroup(result.resultGroupId, result.version)
}

async function checkStatus(): Promise<void> {
  if (!lookup.resultGroupId.trim() || !positiveInteger(lookup.expectedVersion)) {
    state.validate('采用状态查询需要结果组 ID 和正整数精确版本。')
    return
  }
  await state.execute(
    'Result 采用状态',
    () => getResultAdoptionStatus(lookup.resultGroupId.trim(), lookup.expectedVersion, context()),
    checkStatus
  )
}

function readPayload(): JsonRecord | undefined {
  try { return parseJsonObject(payloadText.value) } catch (error) {
    state.validate(error instanceof Error ? error.message : '请求 JSON 无效。')
    return undefined
  }
}

function validatePayload(payload: JsonRecord): boolean {
  if (payload.ruleSetVersion !== RESULT_RULE_SET_VERSION) {
    return state.validate(`规则集必须固定为 ${RESULT_RULE_SET_VERSION}。`)
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
    return state.validate('追加或采用操作需要正整数 expectedCurrentVersion。')
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
      '推导需要聚合规则精确版本、值、单位和至少一个来源输入。')
  }
  if (operation.value === 'rule') {
    const strategy = payload.strategy
    return state.validate((strategy === 'RETEST_REPLACES_ORIGINAL' || strategy === 'TECHNICAL_REVIEW_SELECTS') &&
      hasVersionedReference(payload.ruleRef) ? '' : '采用规则需要批准的策略和规则精确引用。')
  }
  return state.validate(hasRequiredString(payload, 'targetId') ? '' : '采用请求需要目标 ID。')
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
      <p class="eyebrow">LAB WORKBENCH · RESULT · {{ RESULT_RULE_SET_VERSION }}</p>
      <h1>结果来源与采用</h1>
      <p>从门控 Batch 创建结果组，追加观察和推导，冻结采用规则并采用精确结果版本。</p>
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
              <option value="rule">记录采用规则</option>
              <option value="adopt">采用结果</option>
            </select>
          </label>
          <label v-if="operation !== 'create'">结果组 ID
            <input v-model="path.resultGroupId" required :disabled="!canWrite || state.busy.value">
          </label>
        </div>
        <p class="lab-operation-note">重测前先记录采用规则；工作台不会自动选择目标或推导最新版本。</p>
        <LabJsonEditor v-model="payloadText" label="请求 JSON" :disabled="!canWrite || state.busy.value" />
        <div class="lab-actions"><button type="submit" :disabled="!canWrite || state.busy.value">提交写操作</button></div>
      </form>

      <form class="lab-panel" @submit.prevent="loadGroup">
        <h2>结果组详情与采用状态</h2>
        <div class="lab-grid">
          <label>结果组 ID<input v-model="lookup.resultGroupId" required :disabled="state.busy.value"></label>
          <label>结果组精确版本<input v-model.number="lookup.expectedVersion" type="number" min="1" step="1" required :disabled="state.busy.value"></label>
        </div>
        <p v-if="state.validationError.value" class="lab-validation" role="alert">{{ state.validationError.value }}</p>
        <div class="lab-actions">
          <button type="submit" :disabled="state.busy.value">加载结果组</button>
          <button type="button" class="secondary" :disabled="state.busy.value" @click="checkStatus">检查采用状态</button>
        </div>
      </form>

      <LabProblemAlert v-if="state.error.value" :error="state.error.value" @retry="state.retryLast" />
      <LabJsonResult v-if="state.response.value" :title="state.responseTitle.value" :value="state.response.value" :blocked="blockedResponse" />
    </template>
  </main>
</template>

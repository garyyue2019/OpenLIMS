<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'
import { authSnapshot } from '../../auth-store'
import LabAccessNotice from '../lab-workbench/LabAccessNotice.vue'
import LabJsonEditor from '../lab-workbench/LabJsonEditor.vue'
import LabJsonResult from '../lab-workbench/LabJsonResult.vue'
import LabProblemAlert from '../lab-workbench/LabProblemAlert.vue'
import {
  hasArray, hasNonNegativeInteger, hasPositiveInteger, hasRequiredString,
  hasVersionedReference, isJsonRecord, parseJsonObject, prettyJson, type JsonRecord
} from '../lab-workbench/lab-json'
import { useLabOperationState } from '../lab-workbench/lab-operation-state'
import { hasLabCapability } from '../lab-workbench/lab-workbench-access'
import {
  approveToySampleRequirement, createToyTestUnitPlan, getToyTestUnitPlan,
  requestToyAllocation, TOY_TEST_UNIT_RULE_SET_VERSION,
  type ApproveToySampleRequirementRequest, type CreateToyTestUnitPlanRequest,
  type RequestToyAllocationRequest
} from './toy-test-unit-client'

type TestUnitOperation = 'plan' | 'approval' | 'allocation'
const versioned = (id: string) => ({ id, version: 1 })
const samples: Record<TestUnitOperation, JsonRecord> = {
  plan: {
    ruleSetVersion: TOY_TEST_UNIT_RULE_SET_VERSION,
    objectScope: { legalEntityId: 'legal-entity-id', laboratoryId: 'laboratory-id' },
    expectedCurrentVersion: 0, productVersion: 1, ageGradeDecisionVersion: 1,
    accessibilityAssessmentVersion: 1, scopeMatrixId: 'scope-matrix-id', scopeMatrixVersion: 1,
    scopeLineRefs: [versioned('scope-line-id')], sampleRuleRefs: [versioned('sample-rule-id')],
    testUnits: [{
      testUnitId: 'TEST-UNIT-1', physicalObjectRef: versioned('received-item-id'),
      hazardDomainRefs: [versioned('MECHANICAL')], parallelNumber: 1,
      sequenceSteps: [{
        stepId: 'STEP-1', sequenceOrder: 1, taskRef: versioned('task-id'),
        destructive: true, exclusiveDestructiveGroupId: 'DESTRUCTIVE-GROUP-1'
      }]
    }],
    demandInputs: [{
      componentId: 'BASE-1', kind: 'BASE', hazardDomainRef: versioned('MECHANICAL'),
      testUnitId: 'TEST-UNIT-1', amount: 1, dimension: 'COUNT', unit: 'piece',
      sourceRuleRef: versioned('sample-rule-id'), applicability: 'ALLOWED'
    }]
  },
  approval: {
    expectedCurrentVersion: 1, ruleSetVersion: TOY_TEST_UNIT_RULE_SET_VERSION,
    inputHash: 'server-plan-input-hash', approvalComment: 'Technical sample demand approved.'
  },
  allocation: {
    expectedCurrentVersion: 2, ruleSetVersion: TOY_TEST_UNIT_RULE_SET_VERSION,
    quantityChecks: [{
      quantityAccountId: 'quantity-account-id', expectedAccountVersion: 1,
      ruleSetVersion: 'QUANTITY-LEDGER@1.0.0', amount: 1,
      dimension: 'COUNT', unit: 'piece', reservationRef: 'toy-reservation-1'
    }],
    allocationChecks: [{
      allocationId: 'allocation-id', expectedSubjectAllocationVersion: 1,
      ruleSetVersion: 'ALLOCATION-ELIGIBILITY@1.0.0',
      testUnitId: 'TEST-UNIT-1', sequenceStepId: 'STEP-1'
    }]
  }
}

const operation = ref<TestUnitOperation>('plan')
const payloadText = ref(prettyJson(samples.plan))
const path = reactive({ productId: '', planVersion: 1 })
const lookup = reactive({ productId: '', planVersion: 1 })
const authStatus = computed(() => authSnapshot.value.status)
const authenticated = computed(() => authSnapshot.value.status === 'authenticated')
const accessToken = computed(() => authSnapshot.value.user?.access_token ?? '')
const profile = computed(() => authSnapshot.value.user?.profile as Readonly<Record<string, unknown>> | undefined)
const canManage = computed(() => authenticated.value && hasLabCapability(profile.value, 'toy.manage'))
const canApprove = computed(() => authenticated.value && hasLabCapability(profile.value, 'toy.sample-demand.approve'))
const canOperate = computed(() => operation.value === 'approval' ? canApprove.value : canManage.value)
const state = useLabOperationState(authenticated, accessToken)

watch(operation, value => { payloadText.value = prettyJson(samples[value]) })

const blockedResponse = computed(() => {
  if (!isJsonRecord(state.response.value)) return false
  const requirement = state.response.value.requirement
  return state.response.value.state === 'SUPERSEDED' ||
    (isJsonRecord(requirement) && (requirement.decision === 'UNKNOWN' || requirement.decision === 'SUPERSEDED'))
})

async function submitOperation(): Promise<void> {
  const payload = readPayload()
  if (!payload || !validatePayload(payload) || !canOperate.value || !path.productId.trim()) {
    if (!path.productId.trim()) state.validate('TestUnit 写操作需要 product ID。')
    return
  }
  if (operation.value !== 'plan' && !positiveInteger(path.planVersion)) {
    state.validate('批准和分配需要正整数 plan version。')
    return
  }
  const productId = path.productId.trim()
  const result = await state.execute('Toy TestUnit 操作已完成', async () => {
    if (operation.value === 'plan') {
      return createToyTestUnitPlan(productId, payload as unknown as CreateToyTestUnitPlanRequest, context())
    }
    if (operation.value === 'approval') {
      return approveToySampleRequirement(
        productId, path.planVersion, payload as unknown as ApproveToySampleRequirementRequest, context()
      )
    }
    return requestToyAllocation(
      productId, path.planVersion, payload as unknown as RequestToyAllocationRequest, context()
    )
  }, submitOperation)
  if (result) setCurrent(result.productId, result.planVersion)
}

async function loadPlan(): Promise<void> {
  if (!lookup.productId.trim() || !positiveInteger(lookup.planVersion)) {
    state.validate('查询需要 product ID 和正整数 plan version。')
    return
  }
  const result = await state.execute(
    'Toy TestUnitPlan 详情',
    () => getToyTestUnitPlan(lookup.productId.trim(), lookup.planVersion, context()),
    loadPlan
  )
  if (result) setCurrent(result.productId, result.planVersion)
}

function readPayload(): JsonRecord | undefined {
  try { return parseJsonObject(payloadText.value) } catch (error) {
    state.validate(error instanceof Error ? error.message : '请求 JSON 无效。')
    return undefined
  }
}

function validatePayload(payload: JsonRecord): boolean {
  if (payload.ruleSetVersion !== TOY_TEST_UNIT_RULE_SET_VERSION) {
    return state.validate(`规则集必须固定为 ${TOY_TEST_UNIT_RULE_SET_VERSION}。`)
  }
  if (operation.value === 'approval') {
    const valid = hasPositiveInteger(payload, 'expectedCurrentVersion') &&
      hasRequiredString(payload, 'inputHash') && hasRequiredString(payload, 'approvalComment')
    return state.validate(valid ? '' : '需求批准需要正整数版本、服务端输入哈希和非空意见。')
  }
  if (operation.value === 'allocation') return validateAllocation(payload)
  return validatePlan(payload)
}

function validatePlan(payload: JsonRecord): boolean {
  const scope = payload.objectScope
  const valid = hasNonNegativeInteger(payload, 'expectedCurrentVersion') &&
    ['productVersion', 'ageGradeDecisionVersion', 'accessibilityAssessmentVersion', 'scopeMatrixVersion']
      .every(key => hasPositiveInteger(payload, key)) &&
    isJsonRecord(scope) && hasRequiredString(scope, 'legalEntityId') && hasRequiredString(scope, 'laboratoryId') &&
    hasRequiredString(payload, 'scopeMatrixId') && hasArray(payload, 'scopeLineRefs') &&
    (payload.scopeLineRefs as unknown[]).every(hasVersionedReference) && hasArray(payload, 'sampleRuleRefs') &&
    (payload.sampleRuleRefs as unknown[]).every(hasVersionedReference) && hasArray(payload, 'testUnits') &&
    (payload.testUnits as unknown[]).every(validTestUnit) && hasArray(payload, 'demandInputs') &&
    (payload.demandInputs as unknown[]).every(validDemand)
  return state.validate(valid ? '' : '计划需要全部上游精确版本、范围/规则引用、有效 TestUnit 序列和样品需求分量。')
}

function validateAllocation(payload: JsonRecord): boolean {
  const valid = hasPositiveInteger(payload, 'expectedCurrentVersion') &&
    hasArray(payload, 'quantityChecks') && (payload.quantityChecks as unknown[]).every(value =>
      isJsonRecord(value) && hasRequiredString(value, 'quantityAccountId') &&
      hasPositiveInteger(value, 'expectedAccountVersion') && hasRequiredString(value, 'ruleSetVersion') &&
      positiveNumber(value.amount) && hasRequiredString(value, 'dimension') &&
      hasRequiredString(value, 'unit') && hasRequiredString(value, 'reservationRef')) &&
    hasArray(payload, 'allocationChecks') && (payload.allocationChecks as unknown[]).every(value =>
      isJsonRecord(value) && hasRequiredString(value, 'allocationId') &&
      hasPositiveInteger(value, 'expectedSubjectAllocationVersion') && hasRequiredString(value, 'ruleSetVersion') &&
      hasRequiredString(value, 'testUnitId') && hasRequiredString(value, 'sequenceStepId'))
  return state.validate(valid ? '' : '分配需要正整数版本及完整 Quantity 和 Allocation 精确门禁输入。')
}

function validTestUnit(value: unknown): boolean {
  if (!isJsonRecord(value)) return false
  const steps = value.sequenceSteps
  return hasRequiredString(value, 'testUnitId') && hasVersionedReference(value.physicalObjectRef) &&
    hasArray(value, 'hazardDomainRefs') && (value.hazardDomainRefs as unknown[]).every(hasVersionedReference) &&
    hasPositiveInteger(value, 'parallelNumber') && Array.isArray(steps) && steps.length > 0 &&
    steps.every((step, index) => isJsonRecord(step) && hasRequiredString(step, 'stepId') &&
      step.sequenceOrder === index + 1 && hasVersionedReference(step.taskRef) && typeof step.destructive === 'boolean')
}

function validDemand(value: unknown): boolean {
  const kinds = ['BASE', 'PARALLEL', 'EXCLUSIVE_DESTRUCTIVE', 'CHEMICAL_MINIMUM', 'RETEST_RESERVE', 'RETENTION']
  return isJsonRecord(value) && hasRequiredString(value, 'componentId') &&
    typeof value.kind === 'string' && kinds.includes(value.kind) && positiveNumber(value.amount) &&
    hasRequiredString(value, 'dimension') && hasRequiredString(value, 'unit') &&
    hasVersionedReference(value.sourceRuleRef) &&
    typeof value.applicability === 'string' && ['ALLOWED', 'BLOCKED', 'UNKNOWN'].includes(value.applicability)
}

function positiveNumber(value: unknown): boolean { return typeof value === 'number' && Number.isFinite(value) && value > 0 }
function positiveInteger(value: number): boolean { return Number.isInteger(value) && value > 0 }
function setCurrent(productId: string, version: number): void {
  path.productId = productId; path.planVersion = version; lookup.productId = productId; lookup.planVersion = version
}
function context() { return { accessToken: accessToken.value } }
</script>

<template>
  <main class="lab-workbench-page">
    <header class="lab-workbench-heading">
      <p class="eyebrow">TOY WORKBENCH · TESTUNIT · {{ TOY_TEST_UNIT_RULE_SET_VERSION }}</p>
      <h1>TestUnit、样品需求与分配</h1>
      <p>固定危险域、平行号、破坏序列和需求分量，在技术批准后调用精确 Quantity/Allocation 门禁。</p>
    </header>
    <LabAccessNotice :status="authStatus" :can-write="canManage || canApprove" capability="toy.manage / toy.sample-demand.approve" />

    <template v-if="authenticated">
      <form class="lab-panel" @submit.prevent="submitOperation">
        <h2>执行 TestUnit 操作</h2>
        <div class="lab-grid">
          <label>操作<select v-model="operation" :disabled="state.busy.value"><option value="plan">创建计划与需求</option><option value="approval">技术批准需求</option><option value="allocation">请求数量与分配</option></select></label>
          <label>Product ID<input v-model="path.productId" required :disabled="!canOperate || state.busy.value"></label>
          <label v-if="operation !== 'plan'">Plan version<input v-model.number="path.planVersion" type="number" min="1" step="1" required :disabled="!canOperate || state.busy.value"></label>
        </div>
        <p v-if="!canOperate" class="lab-validation" role="status">当前身份缺少 {{ operation === 'approval' ? 'toy.sample-demand.approve' : 'toy.manage' }}。</p>
        <p class="lab-operation-note">UNKNOWN、未批准需求或任一下游门禁阻断都不会被客户端改写为允许。</p>
        <LabJsonEditor v-model="payloadText" label="请求 JSON" :disabled="!canOperate || state.busy.value" />
        <div class="lab-actions"><button type="submit" :disabled="!canOperate || state.busy.value">提交写操作</button></div>
      </form>
      <form class="lab-panel" @submit.prevent="loadPlan">
        <h2>查询 TestUnitPlan</h2>
        <div class="lab-grid"><label>Product ID<input v-model="lookup.productId" required :disabled="!canManage || state.busy.value"></label><label>Plan version<input v-model.number="lookup.planVersion" type="number" min="1" step="1" required :disabled="!canManage || state.busy.value"></label></div>
        <p v-if="state.validationError.value" class="lab-validation" role="alert">{{ state.validationError.value }}</p>
        <div class="lab-actions"><button type="submit" :disabled="!canManage || state.busy.value">查询计划</button></div>
      </form>
      <LabProblemAlert v-if="state.error.value" :error="state.error.value" @retry="state.retryLast" />
      <LabJsonResult v-if="state.response.value" :title="state.responseTitle.value" :value="state.response.value" :blocked="blockedResponse" />
    </template>
  </main>
</template>

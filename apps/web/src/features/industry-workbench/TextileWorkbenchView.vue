<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'
import { authSnapshot } from '../../auth-store'
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
import { hasLabCapability } from '../lab-workbench/lab-workbench-access'
import {
  approveTextileCuttingPlan,
  calculateTextileSampleRequirement,
  createTextileCuttingPlan,
  getTextileCuttingPlan,
  TEXTILE_RULE_SET_VERSION,
  type ApproveTextileCuttingPlanRequest,
  type CreateTextileCuttingPlanRequest,
  type CreateTextileSampleRequirementRequest
} from './textile-client'

type TextileOperation = 'requirement' | 'plan' | 'approval'
const versioned = (id: string) => ({ id, version: 1 })

const samples: Record<TextileOperation, JsonRecord> = {
  requirement: {
    requirementId: 'TEXTILE-REQ-1',
    expectedCurrentVersion: 0,
    objectScope: { legalEntityId: 'legal-entity-id', laboratoryId: 'laboratory-id' },
    calculation: {
      ruleSetVersion: TEXTILE_RULE_SET_VERSION,
      demandLines: [{
        style: versioned('STYLE'), colorway: versioned('RED'), component: versioned('FRONT'),
        material: versioned('COTTON'), position: 'BODY', direction: 'WARP',
        testItem: versioned('TENSILE'), parallelCount: 3, retestReserveCount: 1,
        retentionReserveCount: 1, destructive: true, specimenLengthMm: 10,
        specimenWidthMm: 12, exclusiveDestructiveGroupId: 'GROUP-A'
      }],
      availableFabrics: [{
        style: versioned('STYLE'), colorway: versioned('RED'), component: versioned('FRONT'),
        position: 'BODY', availableAreaSquareMm: 1000
      }]
    }
  },
  plan: {
    cuttingPlanId: 'CUTTING-PLAN-1', expectedCurrentVersion: 0,
    sampleRequirementId: 'TEXTILE-REQ-1', sampleRequirementVersion: 1,
    sampleRequirementInputHash: 'server-requirement-input-hash',
    ruleSetVersion: TEXTILE_RULE_SET_VERSION,
    plan: {
      cuttingPlanId: 'CUTTING-PLAN-1', sourceItem: versioned('FABRIC-LOT'),
      samplingPosition: 'BODY', direction: 'WARP', lengthMm: 10, widthMm: 12,
      plannedCount: 5, minDistanceFromSelvedgeMm: 20, templateVersion: 'TEXTILE-CUT@1.0.0',
      operatorId: 'operator-business-id',
      generatedSpecimenIds: ['SPEC-1', 'SPEC-2', 'SPEC-3', 'SPEC-4', 'SPEC-5']
    }
  },
  approval: {
    expectedCurrentVersion: 1,
    sampleRequirementInputHash: 'server-requirement-input-hash',
    ruleSetVersion: TEXTILE_RULE_SET_VERSION,
    approvalComment: 'Reviewed against the exact requirement version.'
  }
}

const operation = ref<TextileOperation>('requirement')
const payloadText = ref(prettyJson(samples.requirement))
const path = reactive({ cuttingPlanId: '', version: 1 })
const lookup = reactive({ cuttingPlanId: '', version: 1 })
const authStatus = computed(() => authSnapshot.value.status)
const authenticated = computed(() => authSnapshot.value.status === 'authenticated')
const accessToken = computed(() => authSnapshot.value.user?.access_token ?? '')
const profile = computed(() =>
  authSnapshot.value.user?.profile as Readonly<Record<string, unknown>> | undefined
)
const canManage = computed(() => authenticated.value &&
  hasLabCapability(profile.value, 'textile.sample-requirement.manage'))
const canApprove = computed(() => authenticated.value &&
  hasLabCapability(profile.value, 'textile.cutting-plan.approve'))
const canAnyAction = computed(() => canManage.value || canApprove.value)
const canOperate = computed(() => operation.value === 'approval' ? canApprove.value : canManage.value)
const requiredCapability = computed(() => operation.value === 'approval'
  ? 'textile.cutting-plan.approve'
  : 'textile.sample-requirement.manage')
const state = useLabOperationState(authenticated, accessToken)

watch(operation, value => { payloadText.value = prettyJson(samples[value]) })

const blockedResponse = computed(() => {
  if (!isJsonRecord(state.response.value)) return false
  if (state.response.value.state === 'SUPERSEDED') return true
  const result = state.response.value.result
  if (isJsonRecord(result)) {
    return result.decision === 'INSUFFICIENT' || result.decision === 'UNKNOWN'
  }
  const requirement = state.response.value.sampleRequirement
  if (isJsonRecord(requirement) && isJsonRecord(requirement.result)) {
    return requirement.result.decision === 'INSUFFICIENT' || requirement.result.decision === 'UNKNOWN'
  }
  return false
})

async function submitOperation(): Promise<void> {
  const payload = readPayload()
  if (!payload || !validatePayload(payload) || !canOperate.value) return

  if (operation.value === 'approval' &&
      (!path.cuttingPlanId.trim() || !positiveInteger(path.version))) {
    state.validate('批准需要 cutting plan ID 和正整数路径版本。')
    return
  }

  const result = await state.execute('Textile 写操作已完成', async () => {
    if (operation.value === 'requirement') {
      return calculateTextileSampleRequirement(
        payload as unknown as CreateTextileSampleRequirementRequest,
        context()
      )
    }
    if (operation.value === 'plan') {
      return createTextileCuttingPlan(
        payload as unknown as CreateTextileCuttingPlanRequest,
        context()
      )
    }
    return approveTextileCuttingPlan(
      path.cuttingPlanId.trim(),
      path.version,
      payload as unknown as ApproveTextileCuttingPlanRequest,
      context()
    )
  }, submitOperation)

  if (result && 'cuttingPlanId' in result) {
    path.cuttingPlanId = result.cuttingPlanId
    path.version = result.version
    lookup.cuttingPlanId = result.cuttingPlanId
    lookup.version = result.version
  }
}

async function loadPlan(): Promise<void> {
  if (!lookup.cuttingPlanId.trim() || !positiveInteger(lookup.version)) {
    state.validate('查询需要 cutting plan ID 和正整数精确版本。')
    return
  }
  const result = await state.execute(
    'Textile CuttingPlan 详情',
    () => getTextileCuttingPlan(lookup.cuttingPlanId.trim(), lookup.version, context()),
    loadPlan
  )
  if (result) {
    path.cuttingPlanId = result.cuttingPlanId
    path.version = result.version
  }
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
  if (operation.value === 'requirement') return validateRequirement(payload)
  if (payload.ruleSetVersion !== TEXTILE_RULE_SET_VERSION) {
    return state.validate(`规则集必须固定为 ${TEXTILE_RULE_SET_VERSION}。`)
  }
  if (operation.value === 'approval') {
    const valid = hasPositiveInteger(payload, 'expectedCurrentVersion') &&
      hasRequiredString(payload, 'sampleRequirementInputHash')
    return state.validate(valid ? '' : '批准需要正整数 expectedCurrentVersion 和服务端需求输入哈希。')
  }
  return validatePlan(payload)
}

function validateRequirement(payload: JsonRecord): boolean {
  const scope = payload.objectScope
  const calculation = payload.calculation
  const validScope = isJsonRecord(scope) && hasRequiredString(scope, 'legalEntityId') &&
    hasRequiredString(scope, 'laboratoryId')
  if (!hasRequiredString(payload, 'requirementId') ||
      !hasNonNegativeInteger(payload, 'expectedCurrentVersion') || !validScope ||
      !isJsonRecord(calculation) || calculation.ruleSetVersion !== TEXTILE_RULE_SET_VERSION ||
      !hasArray(calculation, 'demandLines') || !hasArray(calculation, 'availableFabrics')) {
    return state.validate('需求计算需要稳定 ID、非负 expectedCurrentVersion、对象范围、固定规则集及非空需求/可用面料数组。')
  }
  const demandLines = calculation.demandLines as unknown[]
  const fabrics = calculation.availableFabrics as unknown[]
  const valid = demandLines.every(validDemandLine) && fabrics.every(validAvailableFabric)
  return state.validate(valid ? '' : '需求行必须固定全部版本引用、批准方向、试样数量/尺寸；可用面积必须为非负数。')
}

function validatePlan(payload: JsonRecord): boolean {
  const plan = payload.plan
  const validHeader = hasRequiredString(payload, 'cuttingPlanId') &&
    hasNonNegativeInteger(payload, 'expectedCurrentVersion') &&
    hasRequiredString(payload, 'sampleRequirementId') &&
    hasPositiveInteger(payload, 'sampleRequirementVersion') &&
    hasRequiredString(payload, 'sampleRequirementInputHash')
  if (!validHeader || !isJsonRecord(plan)) {
    return state.validate('创建计划需要稳定计划/需求 ID、非负并发版本、正整数需求版本、输入哈希和计划对象。')
  }
  const ids = plan.generatedSpecimenIds
  const plannedCount = plan.plannedCount
  const validIds = Array.isArray(ids) && ids.length === plannedCount && ids.length > 0 &&
    ids.every(value => typeof value === 'string' && value.trim()) &&
    new Set(ids).size === ids.length
  const valid = plan.cuttingPlanId === payload.cuttingPlanId &&
    hasVersionedReference(plan.sourceItem) && hasRequiredString(plan, 'samplingPosition') &&
    validDirection(plan.direction) && positiveNumber(plan.lengthMm) && positiveNumber(plan.widthMm) &&
    typeof plannedCount === 'number' && Number.isInteger(plannedCount) && plannedCount > 0 &&
    nonNegativeNumber(plan.minDistanceFromSelvedgeMm) && hasRequiredString(plan, 'templateVersion') &&
    hasRequiredString(plan, 'operatorId') && validIds
  return state.validate(valid ? '' : '计划必须 ID 一致、方向受控、尺寸/数量有效、距布边非负，且生成试样 ID 唯一并与计划数一致。')
}

function validDemandLine(value: unknown): boolean {
  if (!isJsonRecord(value)) return false
  const reserveKeys = ['retestReserveCount', 'retentionReserveCount']
  return ['style', 'colorway', 'component', 'material', 'testItem']
    .every(key => hasVersionedReference(value[key])) &&
    hasRequiredString(value, 'position') && validDirection(value.direction) &&
    hasPositiveInteger(value, 'parallelCount') &&
    reserveKeys.every(key => hasNonNegativeInteger(value, key)) &&
    typeof value.destructive === 'boolean' && positiveNumber(value.specimenLengthMm) &&
    positiveNumber(value.specimenWidthMm) &&
    (value.preconditioning === undefined || hasVersionedReference(value.preconditioning))
}

function validAvailableFabric(value: unknown): boolean {
  return isJsonRecord(value) && ['style', 'colorway', 'component']
    .every(key => hasVersionedReference(value[key])) && hasRequiredString(value, 'position') &&
    nonNegativeNumber(value.availableAreaSquareMm)
}

function validDirection(value: unknown): boolean {
  return typeof value === 'string' && ['WARP', 'WEFT', 'LENGTHWISE', 'CROSSWISE'].includes(value)
}

function positiveNumber(value: unknown): boolean {
  return typeof value === 'number' && Number.isFinite(value) && value > 0
}

function nonNegativeNumber(value: unknown): boolean {
  return typeof value === 'number' && Number.isFinite(value) && value >= 0
}

function positiveInteger(value: number): boolean {
  return Number.isInteger(value) && value > 0
}

function context() { return { accessToken: accessToken.value } }
</script>

<template>
  <main class="lab-workbench-page">
    <header class="lab-workbench-heading">
      <p class="eyebrow">INDUSTRY WORKBENCH · TEXTILE · {{ TEXTILE_RULE_SET_VERSION }}</p>
      <h1>纺织样品需求与 CuttingPlan</h1>
      <p>按精确版本计算试样与面积缺口，创建绑定输入哈希的裁样计划，并执行受控技术批准。</p>
    </header>

    <LabAccessNotice
      :status="authStatus"
      :can-write="canAnyAction"
      capability="textile.sample-requirement.manage / textile.cutting-plan.approve"
    />

    <template v-if="authenticated">
      <section class="lab-panel" aria-labelledby="textile-capabilities-heading">
        <h2 id="textile-capabilities-heading">当前纺织能力</h2>
        <dl class="lab-details">
          <div><dt>需求与计划</dt><dd>{{ canManage ? '允许' : '无 textile.sample-requirement.manage' }}</dd></div>
          <div><dt>计划批准</dt><dd>{{ canApprove ? '允许' : '无 textile.cutting-plan.approve' }}</dd></div>
          <div><dt>失败关闭</dt><dd>INSUFFICIENT / UNKNOWN 均阻断</dd></div>
        </dl>
      </section>

      <form class="lab-panel" @submit.prevent="submitOperation">
        <h2>执行 Textile 写操作</h2>
        <div class="lab-grid">
          <label>操作
            <select v-model="operation" :disabled="state.busy.value">
              <option value="requirement">计算并保存样品需求</option>
              <option value="plan">创建 CuttingPlan</option>
              <option value="approval">批准 CuttingPlan</option>
            </select>
          </label>
          <label v-if="operation === 'approval'">CuttingPlan ID
            <input v-model="path.cuttingPlanId" required :disabled="!canOperate || state.busy.value">
          </label>
          <label v-if="operation === 'approval'">路径精确版本
            <input v-model.number="path.version" type="number" min="1" step="1" required :disabled="!canOperate || state.busy.value">
          </label>
        </div>
        <p v-if="!canOperate" class="lab-validation" role="status">当前身份没有 {{ requiredCapability }} 能力。</p>
        <p class="lab-operation-note">初次创建 expectedCurrentVersion 为 0；对象引用和已存在需求/计划版本必须为正整数。客户端不计算权威输入哈希。</p>
        <LabJsonEditor v-model="payloadText" label="请求 JSON" :disabled="!canOperate || state.busy.value" />
        <div class="lab-actions"><button type="submit" :disabled="!canOperate || state.busy.value">提交写操作</button></div>
      </form>

      <form class="lab-panel" @submit.prevent="loadPlan">
        <h2>查询 CuttingPlan 精确版本</h2>
        <div class="lab-grid">
          <label>CuttingPlan ID<input v-model="lookup.cuttingPlanId" required :disabled="!canManage || state.busy.value"></label>
          <label>精确版本<input v-model.number="lookup.version" type="number" min="1" step="1" required :disabled="!canManage || state.busy.value"></label>
        </div>
        <p v-if="state.validationError.value" class="lab-validation" role="alert">{{ state.validationError.value }}</p>
        <div class="lab-actions"><button type="submit" :disabled="!canManage || state.busy.value">查询计划</button></div>
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

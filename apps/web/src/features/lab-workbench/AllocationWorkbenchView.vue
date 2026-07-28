<script setup lang="ts">
import { reactive, ref } from 'vue'
import LabAccessNotice from './LabAccessNotice.vue'
import LabProblemAlert from './LabProblemAlert.vue'
import type { LabApiError } from './lab-api'
import { normalizeLabError, nonNegativeInteger, positiveInteger, useLabAccess } from './lab-view-state'
import {
  ALLOCATION_RULE_SET_VERSION,
  createTestObjectAllocation,
  getAllocationStatus,
  getTestObjectAllocation,
  releaseTestObjectAllocation,
  type AllocationReleaseResult,
  type AllocationStatusResult,
  type CreateTestObjectAllocationRequest,
  type TestObjectAllocationResult
} from './allocation-client'

const { authStatus, authenticated, accessToken, canWrite } = useLabAccess('allocation.assign')
const form = reactive({
  expectedCurrentVersion: 0,
  legalEntityId: '', laboratoryId: '', customerId: '', serviceOrderId: '', productCategory: '',
  subjectType: 'RECEIVED_ITEM' as CreateTestObjectAllocationRequest['subject']['subjectType'],
  subjectId: '', subjectVersion: 1,
  identityAssignmentId: '', identityAssignmentVersion: 1,
  receivedItemId: '', expectedReceivedItemVersion: 1,
  scopeMatrixId: '', expectedScopeMatrixVersion: 1, scopeLineId: '',
  planStepId: '', planStepVersion: 1, purpose: '', sequenceOrder: 1, destructive: false,
  quantityAccountId: '', expectedQuantityAccountVersion: 1, requestedAmount: 0,
  dimension: 'MASS', unit: 'GRAM',
  storageConditionId: '', storageConditionVersion: 1,
  validUntil: new Date(Date.now() + 7 * 86_400_000).toISOString().slice(0, 16),
  reservationEntryId: ''
})
const lookup = reactive({ allocationId: '', expectedVersion: 1, releaseReason: '' })
const busy = ref(false)
const validationError = ref('')
const error = ref<LabApiError>()
const allocation = ref<TestObjectAllocationResult>()
const release = ref<AllocationReleaseResult>()
const status = ref<AllocationStatusResult>()
let retryAction: (() => Promise<void>) | undefined

async function createAllocation(): Promise<void> {
  const request = buildRequest()
  if (!request) return
  await execute(async () => {
    allocation.value = await createTestObjectAllocation(request, { accessToken: accessToken.value })
    lookup.allocationId = allocation.value.allocationId
    lookup.expectedVersion = allocation.value.subjectAllocationVersion
    release.value = undefined
    status.value = undefined
  }, createAllocation)
}

async function loadAllocation(): Promise<void> {
  validationError.value = ''
  if (!lookup.allocationId.trim()) {
    validationError.value = '请输入分配 ID。'
    return
  }
  await execute(async () => {
    allocation.value = await getTestObjectAllocation(lookup.allocationId.trim(), { accessToken: accessToken.value })
    lookup.expectedVersion = allocation.value.subjectAllocationVersion
  }, loadAllocation)
}

async function checkStatus(): Promise<void> {
  validationError.value = ''
  if (!lookup.allocationId.trim() || !positiveInteger(lookup.expectedVersion)) {
    validationError.value = '状态检查需要分配 ID 和大于 0 的主体分配精确版本。'
    return
  }
  await execute(async () => {
    status.value = await getAllocationStatus(
      lookup.allocationId.trim(), lookup.expectedVersion, { accessToken: accessToken.value }
    )
  }, checkStatus)
}

async function releaseAllocation(): Promise<void> {
  validationError.value = ''
  if (!canWrite.value || !lookup.allocationId.trim() || !lookup.releaseReason.trim()) {
    validationError.value = '释放分配需要分配 ID、明确原因和 allocation.assign 能力。'
    return
  }
  await execute(async () => {
    release.value = await releaseTestObjectAllocation(
      lookup.allocationId.trim(), lookup.releaseReason.trim(), { accessToken: accessToken.value }
    )
    if (allocation.value?.allocationId === release.value.allocationId) {
      allocation.value.state = 'RELEASED'
      allocation.value.releaseReason = release.value.reason
      allocation.value.releasedBy = release.value.releasedBy
      allocation.value.releasedAt = release.value.releasedAt
    }
  }, releaseAllocation)
}

function buildRequest(): CreateTestObjectAllocationRequest | undefined {
  validationError.value = ''
  const requiredStrings = [
    form.legalEntityId, form.laboratoryId, form.customerId, form.serviceOrderId, form.productCategory,
    form.subjectId, form.identityAssignmentId, form.receivedItemId, form.scopeMatrixId,
    form.scopeLineId, form.planStepId, form.purpose, form.quantityAccountId,
    form.dimension, form.unit, form.storageConditionId, form.validUntil
  ]
  const positiveVersions = [
    form.subjectVersion, form.identityAssignmentVersion, form.expectedReceivedItemVersion,
    form.expectedScopeMatrixVersion, form.planStepVersion, form.sequenceOrder,
    form.expectedQuantityAccountVersion, form.storageConditionVersion
  ]
  const validDate = Number.isFinite(new Date(form.validUntil).getTime())
  if (!requiredStrings.every(value => value.trim()) || !nonNegativeInteger(form.expectedCurrentVersion) ||
      !positiveVersions.every(positiveInteger) || form.requestedAmount <= 0 || !validDate) {
    validationError.value = '请填写全部必填字段；引用/顺序版本必须为正整数，请求量必须大于 0，并提供有效截止时间。'
    return undefined
  }
  return {
    expectedCurrentVersion: form.expectedCurrentVersion,
    ruleSetVersion: ALLOCATION_RULE_SET_VERSION,
    objectScope: {
      legalEntityId: form.legalEntityId.trim(), laboratoryId: form.laboratoryId.trim(),
      customerId: form.customerId.trim(), serviceOrderId: form.serviceOrderId.trim(),
      productCategory: form.productCategory.trim()
    },
    subject: { subjectType: form.subjectType, id: form.subjectId.trim(), version: form.subjectVersion },
    identityAssignment: { id: form.identityAssignmentId.trim(), version: form.identityAssignmentVersion },
    receivedItemId: form.receivedItemId.trim(),
    expectedReceivedItemVersion: form.expectedReceivedItemVersion,
    scopeMatrixId: form.scopeMatrixId.trim(), expectedScopeMatrixVersion: form.expectedScopeMatrixVersion,
    scopeLineId: form.scopeLineId.trim(),
    planStep: { id: form.planStepId.trim(), version: form.planStepVersion },
    purpose: form.purpose.trim(), sequenceOrder: form.sequenceOrder, destructive: form.destructive,
    quantityAccountId: form.quantityAccountId.trim(),
    expectedQuantityAccountVersion: form.expectedQuantityAccountVersion,
    requestedAmount: form.requestedAmount, dimension: form.dimension.trim(), unit: form.unit.trim(),
    storageCondition: { id: form.storageConditionId.trim(), version: form.storageConditionVersion },
    validUntil: new Date(form.validUntil).toISOString(),
    reservationEntryId: form.reservationEntryId.trim() || undefined
  }
}

async function execute(action: () => Promise<void>, retry: () => Promise<void>): Promise<void> {
  if (!authenticated.value || !accessToken.value || busy.value) return
  busy.value = true
  error.value = undefined
  retryAction = retry
  try { await action() } catch (caught) { error.value = normalizeLabError(caught) } finally { busy.value = false }
}

function retryLast(): void { if (retryAction && !busy.value) void retryAction() }
</script>

<template>
  <main class="lab-workbench-page">
    <header class="lab-workbench-heading">
      <p class="eyebrow">LAB WORKBENCH · ALLOCATION · {{ ALLOCATION_RULE_SET_VERSION }}</p>
      <h1>样品分配</h1>
      <p>将收样、范围和数量的精确版本绑定为测试对象分配；所有门控决定由服务器返回。</p>
    </header>
    <LabAccessNotice :status="authStatus" :can-write="canWrite" capability="allocation.assign" />

    <template v-if="authenticated">
      <form class="lab-panel" @submit.prevent="createAllocation">
        <h2>创建版本绑定分配</h2>
        <div class="lab-grid">
          <label>主体当前分配版本<input v-model.number="form.expectedCurrentVersion" type="number" min="0" step="1" required :disabled="!canWrite || busy"></label>
          <label>法人<input v-model="form.legalEntityId" required :disabled="!canWrite || busy"></label>
          <label>实验室<input v-model="form.laboratoryId" required :disabled="!canWrite || busy"></label>
          <label>客户<input v-model="form.customerId" required :disabled="!canWrite || busy"></label>
          <label>服务委托<input v-model="form.serviceOrderId" required :disabled="!canWrite || busy"></label>
          <label>产品类别<input v-model="form.productCategory" required :disabled="!canWrite || busy"></label>
          <label>主体类型<select v-model="form.subjectType" :disabled="!canWrite || busy"><option>RECEIVED_ITEM</option><option>TEST_SPECIMEN</option><option>TEST_PORTION</option></select></label>
          <label>主体 ID<input v-model="form.subjectId" required :disabled="!canWrite || busy"></label>
          <label>主体版本<input v-model.number="form.subjectVersion" type="number" min="1" step="1" required :disabled="!canWrite || busy"></label>
          <label>身份判定 ID<input v-model="form.identityAssignmentId" required :disabled="!canWrite || busy"></label>
          <label>身份判定版本<input v-model.number="form.identityAssignmentVersion" type="number" min="1" step="1" required :disabled="!canWrite || busy"></label>
          <label>收样实物 ID<input v-model="form.receivedItemId" required :disabled="!canWrite || busy"></label>
          <label>收样精确版本<input v-model.number="form.expectedReceivedItemVersion" type="number" min="1" step="1" required :disabled="!canWrite || busy"></label>
          <label>范围矩阵 ID<input v-model="form.scopeMatrixId" required :disabled="!canWrite || busy"></label>
          <label>范围矩阵版本<input v-model.number="form.expectedScopeMatrixVersion" type="number" min="1" step="1" required :disabled="!canWrite || busy"></label>
          <label>范围行 ID<input v-model="form.scopeLineId" required :disabled="!canWrite || busy"></label>
          <label>计划步骤 ID<input v-model="form.planStepId" required :disabled="!canWrite || busy"></label>
          <label>计划步骤版本<input v-model.number="form.planStepVersion" type="number" min="1" step="1" required :disabled="!canWrite || busy"></label>
          <label>用途<input v-model="form.purpose" required :disabled="!canWrite || busy"></label>
          <label>顺序<input v-model.number="form.sequenceOrder" type="number" min="1" step="1" required :disabled="!canWrite || busy"></label>
          <label><span>破坏性分配</span><input v-model="form.destructive" type="checkbox" :disabled="!canWrite || busy"></label>
          <label>数量账户 ID<input v-model="form.quantityAccountId" required :disabled="!canWrite || busy"></label>
          <label>数量账户版本<input v-model.number="form.expectedQuantityAccountVersion" type="number" min="1" step="1" required :disabled="!canWrite || busy"></label>
          <label>请求量<input v-model.number="form.requestedAmount" type="number" min="0" step="any" required :disabled="!canWrite || busy"></label>
          <label>维度<input v-model="form.dimension" required :disabled="!canWrite || busy"></label>
          <label>单位<input v-model="form.unit" required :disabled="!canWrite || busy"></label>
          <label>存储条件 ID<input v-model="form.storageConditionId" required :disabled="!canWrite || busy"></label>
          <label>存储条件版本<input v-model.number="form.storageConditionVersion" type="number" min="1" step="1" required :disabled="!canWrite || busy"></label>
          <label>有效期<input v-model="form.validUntil" type="datetime-local" required :disabled="!canWrite || busy"></label>
          <label>预留流水 ID<input v-model="form.reservationEntryId" :disabled="!canWrite || busy"></label>
        </div>
        <div class="lab-actions"><button type="submit" :disabled="!canWrite || busy">创建分配</button></div>
      </form>

      <form class="lab-panel" @submit.prevent="loadAllocation">
        <h2>分配详情、状态与释放</h2>
        <div class="lab-grid">
          <label>分配 ID<input v-model="lookup.allocationId" required :disabled="busy"></label>
          <label>主体分配精确版本<input v-model.number="lookup.expectedVersion" type="number" min="1" step="1" required :disabled="busy"></label>
          <label>释放原因<input v-model="lookup.releaseReason" :disabled="!canWrite || busy"></label>
        </div>
        <p v-if="validationError" class="lab-validation" role="alert">{{ validationError }}</p>
        <div class="lab-actions">
          <button type="submit" :disabled="busy">加载详情</button>
          <button type="button" class="secondary" :disabled="busy" @click="checkStatus">检查状态</button>
          <button type="button" :disabled="!canWrite || busy || !lookup.releaseReason.trim()" @click="releaseAllocation">释放分配</button>
        </div>
      </form>

      <LabProblemAlert v-if="error" :error="error" @retry="retryLast" />

      <section v-if="allocation" class="lab-panel lab-result" aria-live="polite">
        <h2>分配详情</h2>
        <dl class="lab-details">
          <div><dt>分配 ID</dt><dd>{{ allocation.allocationId }}</dd></div>
          <div><dt>状态</dt><dd>{{ allocation.state }}</dd></div>
          <div><dt>主体分配版本</dt><dd>{{ allocation.subjectAllocationVersion }}</dd></div>
          <div><dt>范围矩阵</dt><dd>{{ allocation.scopeMatrixId }} · {{ allocation.scopeLineId }}</dd></div>
          <div><dt>数量账户</dt><dd>{{ allocation.quantityAccountId }}</dd></div>
          <div><dt>请求量</dt><dd>{{ allocation.requestedAmount }} {{ allocation.unit }}</dd></div>
        </dl>
        <h3>门控证据</h3>
        <ul>
          <li v-for="gate in [allocation.receivingGate, allocation.scopeGate, allocation.quantityGate]" :key="gate.source">
            {{ gate.source }}：{{ gate.decision }} · 版本 {{ gate.pinnedVersion ?? '不可用' }} · 原因 {{ gate.reasonCodes.join('、') || '无' }}
          </li>
        </ul>
      </section>

      <section v-if="release" class="lab-panel lab-result" aria-live="polite"><h2>分配已释放</h2><p>{{ release.allocationId }} · {{ release.reason }} · {{ release.releasedAt }}</p></section>

      <section v-if="status" class="lab-panel" :class="status.decision === 'ALLOWED' ? 'lab-result' : 'lab-blocked'" aria-live="polite">
        <h2>分配状态决定：{{ status.decision }}</h2>
        <p>业务状态：{{ status.state ?? '不可用' }} · 当前版本：{{ status.currentSubjectAllocationVersion ?? '不可用' }}</p>
        <p>原因码：{{ status.reasonCodes.join('、') || '无' }}</p>
      </section>
    </template>
  </main>
</template>

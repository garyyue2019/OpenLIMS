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
  changeWorkTaskState,
  createLineageEdge,
  createWorkPlan,
  getCustodyChain,
  getSampleLineage,
  getWorkPlan,
  getWorkQueue,
  recordCustodyEvent,
  reserveWorkResource,
  type ChangeWorkTaskStateRequest,
  type CreateLineageEdgeRequest,
  type CreateWorkPlanRequest,
  type RecordCustodyEventRequest,
  type ReserveResourceRequest
} from './operations-client'

type OperationsOperation = 'lineage' | 'custody' | 'plan' | 'task-state' | 'reserve'
type OperationsLookup = 'lineage' | 'custody' | 'plan' | 'queue'

const ref1 = { id: 'versioned-ref-id', version: 1 }
const objectScope = {
  legalEntityId: 'legal-entity-id', laboratoryId: 'laboratory-id',
  customerId: 'customer-id', serviceOrderId: 'service-order-id', productCategory: 'TOYS'
}
const samples: Record<OperationsOperation, JsonRecord> = {
  lineage: {
    sourceObjectId: 'sample-source-id', targetObjectId: 'sample-target-id',
    relationKind: 'ALIQUOT', basis: ref1, objectScope
  },
  custody: {
    objectId: 'sample-id', eventKind: 'TRANSFER', fromLocationId: 'cold-room',
    toLocationId: 'workbench', responsiblePartyId: 'analyst-id', evidenceRef: 'scan-id', objectScope
  },
  plan: {
    scopeMatrix: ref1, sampleIdentity: ref1,
    tasks: [{
      taskId: 'task-id', scopeLineId: 'scope-line-id', method: ref1,
      workCenterId: 'work-center-id', priority: 5, sequence: 1, destructive: false,
      dependencyTaskIds: []
    }],
    objectScope
  },
  'task-state': { expectedPlanVersion: 1, state: 'READY', reason: '依赖任务已完成' },
  reserve: {
    expectedPlanVersion: 2, taskId: 'task-id', resourceKind: 'INSTRUMENT',
    resourceId: 'instrument-id', startsAt: '2026-08-05T01:00:00Z', endsAt: '2026-08-05T02:00:00Z'
  }
}

const { authStatus, authenticated, accessToken, canWrite } = useLabAccess('operations:write')
const operation = ref<OperationsOperation>('plan')
const payloadText = ref(prettyJson(samples.plan))
const path = reactive({ workPlanId: '', taskId: '' })
const lookup = reactive<{
  kind: OperationsLookup
  objectId: string
  workPlanId: string
  workCenterId: string
  state: string
}>({ kind: 'queue', objectId: '', workPlanId: '', workCenterId: '', state: '' })
const state = useLabOperationState(authenticated, accessToken)

watch(operation, value => { payloadText.value = prettyJson(samples[value]) })

const blockedResponse = computed(() => isJsonRecord(state.response.value) &&
  ['BLOCKED', 'UNKNOWN'].includes(String(state.response.value.decision ?? state.response.value.state)))

async function submitOperation(): Promise<void> {
  const payload = readPayload()
  if (!payload || !validatePayload(payload) || !validatePath()) return

  const result = await state.execute('样品作业写入已完成', async () => {
    if (operation.value === 'lineage') {
      return createLineageEdge(payload as unknown as CreateLineageEdgeRequest, context())
    }
    if (operation.value === 'custody') {
      return recordCustodyEvent(payload as unknown as RecordCustodyEventRequest, context())
    }
    if (operation.value === 'plan') {
      return createWorkPlan(payload as unknown as CreateWorkPlanRequest, context())
    }
    if (operation.value === 'task-state') {
      return changeWorkTaskState(
        path.workPlanId.trim(), path.taskId.trim(), payload as unknown as ChangeWorkTaskStateRequest, context()
      )
    }
    return reserveWorkResource(
      path.workPlanId.trim(), payload as unknown as ReserveResourceRequest, context()
    )
  }, submitOperation)

  if (isJsonRecord(result) && typeof result.workPlanId === 'string') {
    path.workPlanId = result.workPlanId
    lookup.workPlanId = result.workPlanId
  }
}

async function loadObject(): Promise<void> {
  if (lookup.kind === 'plan') {
    if (!lookup.workPlanId.trim()) { state.validate('请输入 work plan ID。'); return }
    await state.execute('作业计划详情', () => getWorkPlan(lookup.workPlanId.trim(), context()), loadObject)
    return
  }
  if (lookup.kind === 'queue') {
    if (!lookup.workCenterId.trim()) { state.validate('工作队列查询需要 work center ID。'); return }
    await state.execute(
      '工作队列',
      () => getWorkQueue(lookup.workCenterId.trim(), lookup.state.trim() || undefined, context()),
      loadObject
    )
    return
  }
  if (!lookup.objectId.trim()) { state.validate('请输入样品或对象 ID。'); return }
  if (lookup.kind === 'lineage') {
    await state.execute(
      '样品谱系', () => getSampleLineage(lookup.objectId.trim(), context()), loadObject
    )
    return
  }
  await state.execute(
    '监管链', () => getCustodyChain(lookup.objectId.trim(), context()), loadObject
  )
}

function readPayload(): JsonRecord | undefined {
  try { return parseJsonObject(payloadText.value) } catch (error) {
    state.validate(error instanceof Error ? error.message : '请求 JSON 无效。')
    return undefined
  }
}

function validatePath(): boolean {
  if (operation.value === 'task-state' || operation.value === 'reserve') {
    if (!path.workPlanId.trim()) return state.validate('该操作需要 work plan ID。')
  }
  if (operation.value === 'task-state' && !path.taskId.trim()) {
    return state.validate('任务状态变更需要 task ID。')
  }
  return true
}

function validatePayload(payload: JsonRecord): boolean {
  if (operation.value === 'lineage') {
    return state.validate(hasRequiredString(payload, 'sourceObjectId') &&
      hasRequiredString(payload, 'targetObjectId') && hasRequiredString(payload, 'relationKind') &&
      hasVersionedReference(payload.basis) && validScope(payload.objectScope) ? '' :
      '谱系边需要来源、目标、关系类型、依据版本和完整对象范围。')
  }
  if (operation.value === 'custody') {
    return state.validate(hasRequiredString(payload, 'objectId') && hasRequiredString(payload, 'eventKind') &&
      hasRequiredString(payload, 'toLocationId') && hasRequiredString(payload, 'responsiblePartyId') &&
      hasRequiredString(payload, 'evidenceRef') && validScope(payload.objectScope) ? '' :
      '监管链事件需要对象、事件、目标位置、责任人、证据和完整对象范围。')
  }
  if (operation.value === 'plan') {
    return state.validate(hasVersionedReference(payload.scopeMatrix) &&
      hasVersionedReference(payload.sampleIdentity) && hasArray(payload, 'tasks') &&
      validScope(payload.objectScope) ? '' : '作业计划需要范围矩阵、样品身份、任务和完整对象范围。')
  }
  if (operation.value === 'task-state') {
    return state.validate(hasPositiveInteger(payload, 'expectedPlanVersion') &&
      hasRequiredString(payload, 'state') && hasRequiredString(payload, 'reason') ? '' :
      '任务状态变更需要计划精确版本、目标状态和原因。')
  }
  return state.validate(hasPositiveInteger(payload, 'expectedPlanVersion') &&
    hasRequiredString(payload, 'taskId') && hasRequiredString(payload, 'resourceKind') &&
    hasRequiredString(payload, 'resourceId') && hasRequiredString(payload, 'startsAt') &&
    hasRequiredString(payload, 'endsAt') ? '' : '资源预留需要计划版本、任务、资源和完整时间窗口。')
}

function validScope(value: unknown): boolean {
  return isJsonRecord(value) && [
    'legalEntityId', 'laboratoryId', 'customerId', 'serviceOrderId', 'productCategory'
  ].every(key => hasRequiredString(value, key))
}

function context() { return { accessToken: accessToken.value } }
</script>

<template>
  <main class="lab-workbench-page">
    <header class="lab-workbench-heading">
      <p class="eyebrow">RELEASE WORKBENCH / OPERATIONS</p>
      <h1>样品谱系与作业计划</h1>
      <p>记录物理谱系和监管链，编制作业计划、推进任务状态、预留资源并读取确定性工作队列。</p>
    </header>
    <LabAccessNotice :status="authStatus" :can-write="canWrite" capability="operations:write" />

    <template v-if="authenticated">
      <form class="lab-panel" @submit.prevent="submitOperation">
        <h2>执行样品作业写入</h2>
        <div class="lab-grid">
          <label>操作
            <select v-model="operation" :disabled="!canWrite || state.busy.value">
              <option value="lineage">创建谱系边</option>
              <option value="custody">记录监管链事件</option>
              <option value="plan">创建作业计划</option>
              <option value="task-state">变更任务状态</option>
              <option value="reserve">预留作业资源</option>
            </select>
          </label>
          <label v-if="operation === 'task-state' || operation === 'reserve'">Work plan ID
            <input v-model="path.workPlanId" required :disabled="!canWrite || state.busy.value">
          </label>
          <label v-if="operation === 'task-state'">Task ID
            <input v-model="path.taskId" required :disabled="!canWrite || state.busy.value">
          </label>
        </div>
        <p class="lab-operation-note">页面不重排依赖、不推导可执行状态，也不覆盖资源冲突；服务端响应是唯一作业事实。</p>
        <LabJsonEditor v-model="payloadText" label="请求 JSON" :disabled="!canWrite || state.busy.value" />
        <div class="lab-actions">
          <button type="submit" :disabled="!canWrite || state.busy.value">
            {{ state.busy.value ? '处理中...' : '提交写入' }}
          </button>
        </div>
      </form>

      <form class="lab-panel" @submit.prevent="loadObject">
        <h2>读取谱系、计划与队列</h2>
        <div class="lab-grid">
          <label>读取类型
            <select v-model="lookup.kind" :disabled="state.busy.value">
              <option value="lineage">样品谱系</option>
              <option value="custody">监管链</option>
              <option value="plan">作业计划</option>
              <option value="queue">工作队列</option>
            </select>
          </label>
          <label v-if="lookup.kind === 'lineage' || lookup.kind === 'custody'">对象 ID
            <input v-model="lookup.objectId" required :disabled="state.busy.value">
          </label>
          <label v-if="lookup.kind === 'plan'">Work plan ID
            <input v-model="lookup.workPlanId" required :disabled="state.busy.value">
          </label>
          <template v-if="lookup.kind === 'queue'">
            <label>Work center ID<input v-model="lookup.workCenterId" required :disabled="state.busy.value"></label>
            <label>状态筛选（可选）<input v-model="lookup.state" :disabled="state.busy.value"></label>
          </template>
        </div>
        <p v-if="state.validationError.value" class="lab-validation" role="alert">{{ state.validationError.value }}</p>
        <div class="lab-actions"><button type="submit" :disabled="state.busy.value">加载服务端事实</button></div>
      </form>

      <p v-if="state.busy.value" class="lab-panel" role="status">正在等待服务端响应...</p>
      <p v-else-if="!state.response.value && !state.error.value" class="lab-panel lab-empty">尚未加载作业数据。</p>
      <LabProblemAlert v-if="state.error.value" :error="state.error.value" @retry="state.retryLast" />
      <LabJsonResult v-if="state.response.value" :title="state.responseTitle.value" :value="state.response.value" :blocked="blockedResponse" />
    </template>
  </main>
</template>

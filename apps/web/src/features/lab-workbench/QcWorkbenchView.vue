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
  hasVersionedReference,
  isJsonRecord,
  parseJsonObject,
  prettyJson,
  type JsonRecord
} from './lab-json'
import { useLabOperationState } from './lab-operation-state'
import { positiveInteger, useLabAccess } from './lab-view-state'
import {
  getQcReportability,
  getQcRun,
  openQcRun,
  QC_RULE_SET_VERSION,
  recordQcDeviationApproval,
  recordQcImpact,
  recordQcResult,
  recordQcVerdict,
  releaseQcBlock,
  satisfyQcReleaseGate,
  type AddQcResultRequest,
  type CreateQcRunRequest,
  type RecordQcDeviationApprovalRequest,
  type RecordQcImpactRequest,
  type RecordQcVerdictRequest,
  type ReleaseQcBlockRequest,
  type SatisfyQcReleaseGateRequest
} from './qc-client'

type QcOperation = 'create' | 'result' | 'verdict' | 'impact' | 'deviation' | 'gate' | 'release'
const ref1 = { id: 'versioned-ref-id', version: 1 }
const versionRequest = { expectedCurrentVersion: 1, ruleSetVersion: QC_RULE_SET_VERSION }
const samples: Record<QcOperation, JsonRecord> = {
  create: {
    ruleSetVersion: QC_RULE_SET_VERSION,
    objectScope: { legalEntityId: 'legal-entity-id', laboratoryId: 'laboratory-id' },
    batchId: 'batch-id', expectedBatchVersion: 1, method: ref1, qcRuleSet: ref1
  },
  result: {
    ...versionRequest,
    rule: ref1,
    controlType: 'REFERENCE_MATERIAL',
    observedValue: '1.00',
    verdict: 'PASS',
    verdictBasis: 'Within the pinned rule limits.'
  },
  verdict: { ...versionRequest },
  impact: {
    ...versionRequest,
    targets: [{ targetType: 'RESULT_GROUP', targetId: 'result-group-id', targetVersion: 1 }]
  },
  deviation: {
    ...versionRequest,
    approvalRef: ref1,
    reason: 'Approved deviation is recorded but does not release the QC block.'
  },
  gate: {
    ...versionRequest,
    kind: 'INVESTIGATION',
    evidenceRef: ref1
  },
  release: { ...versionRequest }
}

const { authStatus, authenticated, accessToken, canWrite } = useLabAccess('qc.manage')
const operation = ref<QcOperation>('create')
const payloadText = ref(prettyJson(samples.create))
const path = reactive({ qcRunId: '' })
const lookup = reactive({ qcRunId: '', expectedRunVersion: 1, targetId: '' })
const state = useLabOperationState(authenticated, accessToken)

watch(operation, value => { payloadText.value = prettyJson(samples[value]) })

const blockedResponse = computed(() => {
  if (!isJsonRecord(state.response.value)) return false
  return state.response.value.decision === 'BLOCKED' || state.response.value.decision === 'UNKNOWN' ||
    state.response.value.state === 'FAILED'
})

async function submitOperation(): Promise<void> {
  const payload = readPayload()
  if (!payload || !validatePayload(payload)) return
  if (operation.value !== 'create' && !path.qcRunId.trim()) {
    state.validate('追加、判定和放行操作需要 QC run ID。')
    return
  }
  const result = await state.execute('QC 写操作已完成', async () => {
    const id = path.qcRunId.trim()
    if (operation.value === 'create') {
      return openQcRun(payload as unknown as CreateQcRunRequest, context())
    }
    if (operation.value === 'result') {
      return recordQcResult(id, payload as unknown as AddQcResultRequest, context())
    }
    if (operation.value === 'verdict') {
      return recordQcVerdict(id, payload as unknown as RecordQcVerdictRequest, context())
    }
    if (operation.value === 'impact') {
      return recordQcImpact(id, payload as unknown as RecordQcImpactRequest, context())
    }
    if (operation.value === 'deviation') {
      return recordQcDeviationApproval(
        id, payload as unknown as RecordQcDeviationApprovalRequest, context()
      )
    }
    if (operation.value === 'gate') {
      return satisfyQcReleaseGate(id, payload as unknown as SatisfyQcReleaseGateRequest, context())
    }
    return releaseQcBlock(id, payload as unknown as ReleaseQcBlockRequest, context())
  }, submitOperation)
  if (result) setCurrentRun(result.qcRunId, result.version)
}

async function loadRun(): Promise<void> {
  if (!lookup.qcRunId.trim()) { state.validate('请输入 QC run ID。'); return }
  const result = await state.execute(
    'QC run 详情', () => getQcRun(lookup.qcRunId.trim(), context()), loadRun
  )
  if (result) setCurrentRun(result.qcRunId, result.version)
}

async function checkReportability(): Promise<void> {
  if (!lookup.qcRunId.trim() || !positiveInteger(lookup.expectedRunVersion) || !lookup.targetId.trim()) {
    state.validate('可报告性查询需要 QC run ID、正整数精确版本和目标 ID。')
    return
  }
  await state.execute(
    'QC 可报告性决定',
    () => getQcReportability(
      lookup.qcRunId.trim(), lookup.expectedRunVersion, lookup.targetId.trim(), context()
    ),
    checkReportability
  )
}

function readPayload(): JsonRecord | undefined {
  try { return parseJsonObject(payloadText.value) } catch (error) {
    state.validate(error instanceof Error ? error.message : '请求 JSON 无效。')
    return undefined
  }
}

function validatePayload(payload: JsonRecord): boolean {
  if (payload.ruleSetVersion !== QC_RULE_SET_VERSION) {
    return state.validate(`规则集必须固定为 ${QC_RULE_SET_VERSION}。`)
  }
  if (operation.value === 'create') {
    const scope = payload.objectScope
    const valid = isJsonRecord(scope) && hasRequiredString(scope, 'legalEntityId') &&
      hasRequiredString(scope, 'laboratoryId') && hasRequiredString(payload, 'batchId') &&
      hasPositiveInteger(payload, 'expectedBatchVersion') && hasVersionedReference(payload.method) &&
      hasVersionedReference(payload.qcRuleSet)
    return state.validate(valid ? '' : '创建 QC run 需要完整对象范围、Batch 精确版本、方法和 QC 规则集引用。')
  }
  if (!hasPositiveInteger(payload, 'expectedCurrentVersion')) {
    return state.validate('QC 写操作需要正整数 expectedCurrentVersion。')
  }
  if (operation.value === 'result') {
    const valid = hasVersionedReference(payload.rule) && hasRequiredString(payload, 'controlType') &&
      hasRequiredString(payload, 'observedValue') && hasRequiredString(payload, 'verdict') &&
      hasRequiredString(payload, 'verdictBasis')
    return state.validate(valid ? '' : 'QC 结果需要规则精确引用、对照类型、观察值、结论和结论依据。')
  }
  if (operation.value === 'impact') {
    return state.validate(hasArray(payload, 'targets') ? '' : 'QC 影响必须列出至少一个精确版本目标。')
  }
  if (operation.value === 'deviation') {
    return state.validate(hasVersionedReference(payload.approvalRef) && hasRequiredString(payload, 'reason')
      ? '' : '偏差批准需要批准引用精确版本和原因。')
  }
  if (operation.value === 'gate') {
    const allowed = ['INVESTIGATION', 'IMPACT_SCOPE', 'VALIDITY_DECISION', 'ADOPTION_RULE', 'TECHNICAL_REVIEW']
    return state.validate(typeof payload.kind === 'string' && allowed.includes(payload.kind) &&
      hasVersionedReference(payload.evidenceRef) ? '' : '放行门必须是五个批准门之一并绑定证据精确版本。')
  }
  return state.validate('')
}

function setCurrentRun(id: string, version: number): void {
  path.qcRunId = id
  lookup.qcRunId = id
  lookup.expectedRunVersion = version
}

function context() { return { accessToken: accessToken.value } }
</script>

<template>
  <main class="lab-workbench-page">
    <header class="lab-workbench-heading">
      <p class="eyebrow">LAB WORKBENCH · QC · {{ QC_RULE_SET_VERSION }}</p>
      <h1>QC 影响与放行</h1>
      <p>记录 QC 结果和结论，明确完整影响范围，逐一满足五个放行门后解除阻断。</p>
    </header>
    <LabAccessNotice :status="authStatus" :can-write="canWrite" capability="qc.manage" />

    <template v-if="authenticated">
      <form class="lab-panel" @submit.prevent="submitOperation">
        <h2>执行 QC 操作</h2>
        <div class="lab-grid">
          <label>操作
            <select v-model="operation" :disabled="!canWrite || state.busy.value">
              <option value="create">创建 QC run</option>
              <option value="result">追加 QC 结果</option>
              <option value="verdict">记录 run 结论</option>
              <option value="impact">记录影响范围</option>
              <option value="deviation">记录偏差批准</option>
              <option value="gate">满足一个放行门</option>
              <option value="release">解除 QC 阻断</option>
            </select>
          </label>
          <label v-if="operation !== 'create'">QC run ID
            <input v-model="path.qcRunId" required :disabled="!canWrite || state.busy.value">
          </label>
        </div>
        <p class="lab-operation-note">偏差批准不会自动解除阻断；必须显式满足五个放行门后再请求 release。</p>
        <LabJsonEditor v-model="payloadText" label="请求 JSON" :disabled="!canWrite || state.busy.value" />
        <div class="lab-actions"><button type="submit" :disabled="!canWrite || state.busy.value">提交写操作</button></div>
      </form>

      <form class="lab-panel" @submit.prevent="loadRun">
        <h2>QC run 详情与可报告性</h2>
        <div class="lab-grid">
          <label>QC run ID<input v-model="lookup.qcRunId" required :disabled="state.busy.value"></label>
          <label>run 精确版本<input v-model.number="lookup.expectedRunVersion" type="number" min="1" step="1" required :disabled="state.busy.value"></label>
          <label>可报告性目标 ID<input v-model="lookup.targetId" :disabled="state.busy.value"></label>
        </div>
        <p v-if="state.validationError.value" class="lab-validation" role="alert">{{ state.validationError.value }}</p>
        <div class="lab-actions">
          <button type="submit" :disabled="state.busy.value">加载 QC run</button>
          <button type="button" class="secondary" :disabled="state.busy.value" @click="checkReportability">检查可报告性</button>
        </div>
      </form>

      <LabProblemAlert v-if="state.error.value" :error="state.error.value" @retry="state.retryLast" />
      <LabJsonResult v-if="state.response.value" :title="state.responseTitle.value" :value="state.response.value" :blocked="blockedResponse" />
    </template>
  </main>
</template>

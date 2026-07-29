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
  addReportLine,
  createReport,
  evaluateReportGate,
  getReport,
  getReportIssuanceGate,
  getReportPendingContentHash,
  getReportVerification,
  getReportVersion,
  issueReport,
  performReportControlledAction,
  REPORT_RULE_SET_VERSION,
  submitReportForApproval,
  type AddReportLineRequest,
  type CreateReportRequest,
  type EvaluateReportGateRequest,
  type IssueReportRequest,
  type PerformControlledActionRequest,
  type SubmitReportForApprovalRequest
} from './report-client'

type ReportOperation = 'create' | 'line' | 'evaluate' | 'submit' | 'issue' | 'action'
const ref1 = { id: 'versioned-ref-id', version: 1 }
const versionRequest = { expectedCurrentVersion: 1, ruleSetVersion: REPORT_RULE_SET_VERSION }
const samples: Record<ReportOperation, JsonRecord> = {
  create: {
    ruleSetVersion: REPORT_RULE_SET_VERSION,
    objectScope: {
      legalEntityId: 'legal-entity-id', laboratoryId: 'laboratory-id',
      customerId: 'customer-id', serviceOrderId: 'service-order-id', productCategory: 'PRODUCT'
    },
    reportNumber: 'REPORT-2026-0001'
  },
  line: {
    ...versionRequest,
    lineNumber: 1,
    resultGroupId: 'result-group-id',
    expectedGroupVersion: 1,
    scopeLineId: 'scope-line-id',
    scopePartition: 'ACTUAL_TESTED',
    traceRefs: {
      batchId: 'batch-id', allocationId: 'allocation-id', receivedItemId: 'received-item-id',
      requirementSnapshot: ref1
    },
    accreditationRef: { ...ref1, sha256: 'd'.repeat(64) },
    accreditationClaim: {
      siteId: 'site-id', method: ref1, productMatrix: 'matrix', parameterRange: 'range',
      validUntil: '2026-12-31T23:59:59Z', signatoryId: 'signatory-id'
    },
    qcRuns: [ref1],
    instrumentFileId: 'instrument-file-id', expectedInstrumentFileVersion: 1,
    expectedReceivedItemVersion: 1, scopeMatrixId: 'scope-matrix-id',
    expectedScopeMatrixVersion: 1, expectedAllocationVersion: 1, expectedBatchVersion: 1,
    claimsAccreditation: true
  },
  evaluate: { ...versionRequest, signatoryId: 'signatory-id' },
  submit: { ...versionRequest },
  issue: {
    ...versionRequest,
    reauthenticationRef: ref1,
    signingIntent: 'I approve issuance of this exact report content.',
    expectedContentHash: 'e'.repeat(64),
    signatoryId: 'signatory-id'
  },
  action: {
    ...versionRequest,
    versionNumber: 1,
    kind: 'WITHDRAWAL',
    reason: 'Controlled withdrawal reason.'
  }
}

const { authStatus, authenticated, accessToken, canWrite } = useLabAccess('report.manage')
const operation = ref<ReportOperation>('create')
const payloadText = ref(prettyJson(samples.create))
const path = reactive({ reportId: '' })
const lookup = reactive({ reportId: '', expectedReportVersion: 1, versionNumber: 1 })
const state = useLabOperationState(authenticated, accessToken)

watch(operation, value => { payloadText.value = prettyJson(samples[value]) })

const blockedResponse = computed(() => {
  if (!isJsonRecord(state.response.value)) return false
  return state.response.value.decision === 'BLOCKED' || state.response.value.decision === 'UNKNOWN' ||
    state.response.value.chainState === 'VOIDED'
})

async function submitOperation(): Promise<void> {
  const payload = readPayload()
  if (!payload || !validatePayload(payload)) return
  if (operation.value !== 'create' && !path.reportId.trim()) {
    state.validate('报告后续操作需要 report ID。')
    return
  }
  const result = await state.execute('Report 写操作已完成', async () => {
    const id = path.reportId.trim()
    if (operation.value === 'create') {
      return createReport(payload as unknown as CreateReportRequest, context())
    }
    if (operation.value === 'line') {
      return addReportLine(id, payload as unknown as AddReportLineRequest, context())
    }
    if (operation.value === 'evaluate') {
      return evaluateReportGate(id, payload as unknown as EvaluateReportGateRequest, context())
    }
    if (operation.value === 'submit') {
      return submitReportForApproval(
        id, payload as unknown as SubmitReportForApprovalRequest, context()
      )
    }
    if (operation.value === 'issue') {
      return issueReport(id, payload as unknown as IssueReportRequest, context())
    }
    return performReportControlledAction(
      id, payload as unknown as PerformControlledActionRequest, context()
    )
  }, submitOperation)
  if (result && 'reportId' in result) {
    path.reportId = result.reportId
    lookup.reportId = result.reportId
    if ('version' in result && typeof result.version === 'number') {
      lookup.expectedReportVersion = result.version
    }
  }
}

async function loadReport(): Promise<void> {
  if (!validReportId()) return
  const result = await state.execute(
    'Report 详情', () => getReport(lookup.reportId.trim(), context()), loadReport
  )
  if (result) setCurrentReport(result.reportId, result.version)
}

async function checkIssuanceGate(): Promise<void> {
  if (!validReportId() || !positiveInteger(lookup.expectedReportVersion)) {
    state.validate('签发门禁查询需要 report ID 和正整数精确版本。')
    return
  }
  await state.execute(
    'Report 签发门禁',
    () => getReportIssuanceGate(
      lookup.reportId.trim(), lookup.expectedReportVersion, context()
    ),
    checkIssuanceGate
  )
}

async function loadPendingHash(): Promise<void> {
  if (!validReportId()) return
  await state.execute(
    'Report 待签内容哈希',
    () => getReportPendingContentHash(lookup.reportId.trim(), context()),
    loadPendingHash
  )
}

async function loadVerification(): Promise<void> {
  if (!validReportId()) return
  await state.execute(
    'Report 版本链验证',
    () => getReportVerification(lookup.reportId.trim(), context()),
    loadVerification
  )
}

async function loadVersion(): Promise<void> {
  if (!validReportId() || !positiveInteger(lookup.versionNumber)) {
    state.validate('历史版本查询需要 report ID 和正整数版本号。')
    return
  }
  await state.execute(
    'Report 历史版本详情',
    () => getReportVersion(lookup.reportId.trim(), lookup.versionNumber, context()),
    loadVersion
  )
}

function readPayload(): JsonRecord | undefined {
  try { return parseJsonObject(payloadText.value) } catch (error) {
    state.validate(error instanceof Error ? error.message : '请求 JSON 无效。')
    return undefined
  }
}

function validatePayload(payload: JsonRecord): boolean {
  if (payload.ruleSetVersion !== REPORT_RULE_SET_VERSION) {
    return state.validate(`规则集必须固定为 ${REPORT_RULE_SET_VERSION}。`)
  }
  if (operation.value === 'create') {
    const scope = payload.objectScope
    const validScope = isJsonRecord(scope) && [
      'legalEntityId', 'laboratoryId', 'customerId', 'serviceOrderId', 'productCategory'
    ].every(key => hasRequiredString(scope, key))
    return state.validate(validScope && hasRequiredString(payload, 'reportNumber') ? '' :
      '创建报告需要完整对象范围和报告编号。')
  }
  if (!hasPositiveInteger(payload, 'expectedCurrentVersion')) {
    return state.validate('报告写操作需要正整数 expectedCurrentVersion。')
  }
  if (operation.value === 'line') {
    const trace = payload.traceRefs
    const accreditation = payload.accreditationRef
    const claim = payload.accreditationClaim
    const valid = hasPositiveInteger(payload, 'lineNumber') && hasRequiredString(payload, 'resultGroupId') &&
      hasPositiveInteger(payload, 'expectedGroupVersion') && hasRequiredString(payload, 'scopeLineId') &&
      hasRequiredString(payload, 'scopePartition') && isJsonRecord(trace) &&
      ['batchId', 'allocationId', 'receivedItemId'].every(key => hasRequiredString(trace, key)) &&
      hasVersionedReference(trace.requirementSnapshot) && isJsonRecord(accreditation) &&
      hasVersionedReference(accreditation) && hasSha256(accreditation, 'sha256') &&
      isJsonRecord(claim) && hasRequiredString(claim, 'siteId') && hasVersionedReference(claim.method) &&
      hasRequiredString(claim, 'productMatrix') && hasRequiredString(claim, 'parameterRange') &&
      hasRequiredString(claim, 'validUntil') && hasRequiredString(claim, 'signatoryId') &&
      hasArray(payload, 'qcRuns') && hasRequiredString(payload, 'instrumentFileId') &&
      ['expectedInstrumentFileVersion', 'expectedReceivedItemVersion', 'expectedScopeMatrixVersion',
        'expectedAllocationVersion', 'expectedBatchVersion'].every(key => hasPositiveInteger(payload, key)) &&
      hasRequiredString(payload, 'scopeMatrixId')
    return state.validate(valid ? '' : '报告行必须完整固定结果、范围、追溯、认可、QC、仪器及全部上游精确版本。')
  }
  if (operation.value === 'evaluate') {
    return state.validate(hasRequiredString(payload, 'signatoryId') ? '' : '门禁评估需要签字人业务 ID。')
  }
  if (operation.value === 'submit') return state.validate('')
  if (operation.value === 'issue') {
    const valid = hasVersionedReference(payload.reauthenticationRef) &&
      hasRequiredString(payload, 'signingIntent') && hasSha256(payload, 'expectedContentHash') &&
      hasRequiredString(payload, 'signatoryId')
    return state.validate(valid ? '' : '签发需要重认证精确引用、签署意图、服务器待签 SHA-256 和签字人业务 ID。')
  }
  const kinds = ['CORRECTION', 'SUPPLEMENT', 'WITHDRAWAL', 'VOID', 'SUPERSESSION']
  const kind = payload.kind
  const needsImpact = kind === 'CORRECTION' || kind === 'SUPPLEMENT'
  const needsSuperseding = kind === 'SUPERSESSION'
  const valid = hasPositiveInteger(payload, 'versionNumber') && typeof kind === 'string' &&
    kinds.includes(kind) && hasRequiredString(payload, 'reason') &&
    (!needsImpact || hasVersionedReference(payload.impactAssessmentRef)) &&
    (!needsSuperseding || hasRequiredString(payload, 'supersedingReportNumber'))
  return state.validate(valid ? '' : '受控动作需要历史版本号、批准动作、原因及该动作要求的影响或替代引用。')
}

function validReportId(): boolean {
  if (lookup.reportId.trim()) return true
  state.validate('请输入 report ID。')
  return false
}

function setCurrentReport(id: string, version: number): void {
  path.reportId = id
  lookup.reportId = id
  lookup.expectedReportVersion = version
}

function context() { return { accessToken: accessToken.value } }
</script>

<template>
  <main class="lab-workbench-page">
    <header class="lab-workbench-heading">
      <p class="eyebrow">LAB WORKBENCH · REPORT · {{ REPORT_RULE_SET_VERSION }}</p>
      <h1>报告门禁与签发</h1>
      <p>组装精确来源链，评估逐项门禁，提交审批，绑定待签哈希，并维护不可变版本链。</p>
    </header>
    <LabAccessNotice :status="authStatus" :can-write="canWrite" capability="report.manage" />

    <template v-if="authenticated">
      <form class="lab-panel" @submit.prevent="submitOperation">
        <h2>执行 Report 操作</h2>
        <div class="lab-grid">
          <label>操作
            <select v-model="operation" :disabled="!canWrite || state.busy.value">
              <option value="create">创建报告草稿</option>
              <option value="line">追加报告行</option>
              <option value="evaluate">评估签发门禁</option>
              <option value="submit">提交审批</option>
              <option value="issue">受控签发</option>
              <option value="action">执行受控动作</option>
            </select>
          </label>
          <label v-if="operation !== 'create'">Report ID
            <input v-model="path.reportId" required :disabled="!canWrite || state.busy.value">
          </label>
        </div>
        <p class="lab-operation-note">签发前先读取服务器待签哈希；客户端不生成权威哈希、签名或签字人身份。</p>
        <LabJsonEditor v-model="payloadText" label="请求 JSON" :disabled="!canWrite || state.busy.value" />
        <div class="lab-actions"><button type="submit" :disabled="!canWrite || state.busy.value">提交写操作</button></div>
      </form>

      <form class="lab-panel" @submit.prevent="loadReport">
        <h2>报告详情、门禁与版本链</h2>
        <div class="lab-grid">
          <label>Report ID<input v-model="lookup.reportId" required :disabled="state.busy.value"></label>
          <label>报告精确版本<input v-model.number="lookup.expectedReportVersion" type="number" min="1" step="1" required :disabled="state.busy.value"></label>
          <label>历史版本号<input v-model.number="lookup.versionNumber" type="number" min="1" step="1" required :disabled="state.busy.value"></label>
        </div>
        <p v-if="state.validationError.value" class="lab-validation" role="alert">{{ state.validationError.value }}</p>
        <div class="lab-actions">
          <button type="submit" :disabled="state.busy.value">加载报告</button>
          <button type="button" class="secondary" :disabled="state.busy.value" @click="checkIssuanceGate">检查签发门禁</button>
          <button type="button" class="secondary" :disabled="state.busy.value" @click="loadPendingHash">读取待签哈希</button>
          <button type="button" class="secondary" :disabled="state.busy.value" @click="loadVerification">读取验证页模型</button>
          <button type="button" class="secondary" :disabled="state.busy.value" @click="loadVersion">读取历史版本</button>
        </div>
      </form>

      <LabProblemAlert v-if="state.error.value" :error="state.error.value" @retry="state.retryLast" />
      <LabJsonResult v-if="state.response.value" :title="state.responseTitle.value" :value="state.response.value" :blocked="blockedResponse" />
    </template>
  </main>
</template>

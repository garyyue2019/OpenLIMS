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
  createReportDelivery,
  createReportDownloadGrant,
  downloadReportVersion,
  evaluateReportGate,
  getReport,
  getReportDelivery,
  getReportIssuanceGate,
  getReportPendingContentHash,
  getReportVerification,
  getReportVersion,
  issueReport,
  performReportControlledAction,
  queueReportNotification,
  recordReportNotificationAttempt,
  REPORT_DELIVERY_RULE_SET_VERSION,
  REPORT_RULE_SET_VERSION,
  submitReportForApproval,
  type AddReportLineRequest,
  type CreateReportDeliveryRequest,
  type CreateReportDownloadGrantRequest,
  type CreateReportRequest,
  type EvaluateReportGateRequest,
  type IssueReportRequest,
  type PerformControlledActionRequest,
  type QueueReportNotificationRequest,
  type RecordReportNotificationAttemptRequest,
  type SubmitReportForApprovalRequest
} from './report-client'

type ReportOperation = 'create' | 'line' | 'evaluate' | 'submit' | 'issue' | 'action' |
  'delivery' | 'grant' | 'notification' | 'notification-attempt'

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
    ...versionRequest, lineNumber: 1, resultGroupId: 'result-group-id', expectedGroupVersion: 1,
    scopeLineId: 'scope-line-id', scopePartition: 'ACTUAL_TESTED',
    traceRefs: {
      batchId: 'batch-id', allocationId: 'allocation-id', receivedItemId: 'received-item-id',
      requirementSnapshot: ref1
    },
    accreditationRef: { ...ref1, sha256: 'd'.repeat(64) },
    accreditationClaim: {
      siteId: 'site-id', method: ref1, productMatrix: 'matrix', parameterRange: 'range',
      validUntil: '2026-12-31T23:59:59Z', signatoryId: 'signatory-id'
    },
    qcRuns: [ref1], instrumentFileId: 'instrument-file-id', expectedInstrumentFileVersion: 1,
    expectedReceivedItemVersion: 1, scopeMatrixId: 'scope-matrix-id',
    expectedScopeMatrixVersion: 1, expectedAllocationVersion: 1, expectedBatchVersion: 1,
    claimsAccreditation: true
  },
  evaluate: { ...versionRequest, signatoryId: 'signatory-id' },
  submit: { ...versionRequest },
  issue: {
    ...versionRequest, reauthenticationRef: ref1,
    signingIntent: 'I approve issuance of this exact report content.',
    expectedContentHash: 'e'.repeat(64), signatoryId: 'signatory-id'
  },
  action: {
    ...versionRequest, versionNumber: 1, kind: 'WITHDRAWAL',
    reason: 'Controlled withdrawal reason.'
  },
  delivery: {
    ruleSetVersion: REPORT_DELIVERY_RULE_SET_VERSION, recipientId: 'recipient-id',
    channel: 'PORTAL', destinationHash: 'f'.repeat(64), idempotencyKey: 'delivery-idempotency-key'
  },
  grant: {
    ruleSetVersion: REPORT_DELIVERY_RULE_SET_VERSION, recipientId: 'recipient-id',
    expiresAt: '2026-08-06T00:00:00Z'
  },
  notification: {
    ruleSetVersion: REPORT_DELIVERY_RULE_SET_VERSION, channel: 'EMAIL',
    destinationHash: 'a'.repeat(64), payload: ref1, idempotencyKey: 'notification-idempotency-key'
  },
  'notification-attempt': {
    ruleSetVersion: REPORT_DELIVERY_RULE_SET_VERSION, idempotencyKey: 'attempt-idempotency-key',
    outcome: 'FAILED', detailCode: 'SMTP_TIMEOUT'
  }
}

const { authStatus, authenticated, accessToken, canWrite } = useLabAccess('report.manage')
const operation = ref<ReportOperation>('create')
const payloadText = ref(prettyJson(samples.create))
const path = reactive({ reportId: '', reportVersionNumber: 1, deliveryId: '', notificationId: '' })
const lookup = reactive({
  reportId: '', expectedReportVersion: 1, versionNumber: 1,
  deliveryId: '', downloadAccessToken: ''
})
const state = useLabOperationState(authenticated, accessToken)

watch(operation, value => { payloadText.value = prettyJson(samples[value]) })

const blockedResponse = computed(() => isJsonRecord(state.response.value) &&
  (state.response.value.decision === 'BLOCKED' || state.response.value.decision === 'UNKNOWN' ||
    state.response.value.chainState === 'VOIDED' || state.response.value.outcome === 'FAILED' ||
    state.response.value.outcome === 'UNKNOWN'))

async function submitOperation(): Promise<void> {
  const payload = readPayload()
  if (!payload || !validatePayload(payload) || !validatePath()) return
  const result = await state.execute('Report 写入已完成', async () => {
    const reportId = path.reportId.trim()
    if (operation.value === 'create') {
      return createReport(payload as unknown as CreateReportRequest, context())
    }
    if (operation.value === 'line') {
      return addReportLine(reportId, payload as unknown as AddReportLineRequest, context())
    }
    if (operation.value === 'evaluate') {
      return evaluateReportGate(reportId, payload as unknown as EvaluateReportGateRequest, context())
    }
    if (operation.value === 'submit') {
      return submitReportForApproval(reportId, payload as unknown as SubmitReportForApprovalRequest, context())
    }
    if (operation.value === 'issue') {
      return issueReport(reportId, payload as unknown as IssueReportRequest, context())
    }
    if (operation.value === 'action') {
      return performReportControlledAction(
        reportId, payload as unknown as PerformControlledActionRequest, context()
      )
    }
    if (operation.value === 'delivery') {
      return createReportDelivery(
        reportId, path.reportVersionNumber,
        payload as unknown as CreateReportDeliveryRequest, context()
      )
    }
    if (operation.value === 'grant') {
      return createReportDownloadGrant(
        path.deliveryId.trim(), payload as unknown as CreateReportDownloadGrantRequest, context()
      )
    }
    if (operation.value === 'notification') {
      return queueReportNotification(
        path.deliveryId.trim(), payload as unknown as QueueReportNotificationRequest, context()
      )
    }
    return recordReportNotificationAttempt(
      path.notificationId.trim(), payload as unknown as RecordReportNotificationAttemptRequest, context()
    )
  }, submitOperation)

  if (isJsonRecord(result)) {
    if (typeof result.reportId === 'string') {
      path.reportId = result.reportId
      lookup.reportId = result.reportId
      if (typeof result.version === 'number') lookup.expectedReportVersion = result.version
    }
    if (typeof result.deliveryId === 'string') {
      path.deliveryId = result.deliveryId
      lookup.deliveryId = result.deliveryId
    }
    if (typeof result.notificationId === 'string') path.notificationId = result.notificationId
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
  if (!validReportVersion()) return
  await state.execute(
    'Report 签发门禁',
    () => getReportIssuanceGate(lookup.reportId.trim(), lookup.expectedReportVersion, context()),
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

async function loadDelivery(): Promise<void> {
  if (!lookup.deliveryId.trim()) { state.validate('请输入 delivery ID。'); return }
  const result = await state.execute(
    'Report 交付详情', () => getReportDelivery(lookup.deliveryId.trim(), context()), loadDelivery
  )
  if (isJsonRecord(result) && typeof result.deliveryId === 'string') path.deliveryId = result.deliveryId
}

async function downloadVersion(): Promise<void> {
  if (!lookup.downloadAccessToken.trim()) { state.validate('请输入下载授权 token。'); return }
  await state.execute(
    'Report 下载结果',
    () => downloadReportVersion(lookup.downloadAccessToken.trim(), context()),
    downloadVersion
  )
}

function readPayload(): JsonRecord | undefined {
  try { return parseJsonObject(payloadText.value) } catch (error) {
    state.validate(error instanceof Error ? error.message : '请求 JSON 无效。')
    return undefined
  }
}

function validatePath(): boolean {
  if (['line', 'evaluate', 'submit', 'issue', 'action', 'delivery'].includes(operation.value) &&
      !path.reportId.trim()) return state.validate('该操作需要 report ID。')
  if (operation.value === 'delivery' && !positiveInteger(path.reportVersionNumber)) {
    return state.validate('创建交付需要正整数报告版本号。')
  }
  if (['grant', 'notification'].includes(operation.value) && !path.deliveryId.trim()) {
    return state.validate('该操作需要 delivery ID。')
  }
  if (operation.value === 'notification-attempt' && !path.notificationId.trim()) {
    return state.validate('记录通知尝试需要 notification ID。')
  }
  return true
}

function validatePayload(payload: JsonRecord): boolean {
  const deliveryOperation = ['delivery', 'grant', 'notification', 'notification-attempt'].includes(operation.value)
  const expectedRuleSet = deliveryOperation ? REPORT_DELIVERY_RULE_SET_VERSION : REPORT_RULE_SET_VERSION
  if (payload.ruleSetVersion !== expectedRuleSet) {
    return state.validate(`规则集必须固定为 ${expectedRuleSet}。`)
  }
  if (operation.value === 'delivery') {
    return state.validate(hasRequiredString(payload, 'recipientId') && hasRequiredString(payload, 'channel') &&
      hasSha256(payload, 'destinationHash') && hasRequiredString(payload, 'idempotencyKey') ? '' :
      '创建交付需要接收方、渠道、目标 SHA-256 和幂等键。')
  }
  if (operation.value === 'grant') {
    return state.validate(hasRequiredString(payload, 'recipientId') && hasRequiredString(payload, 'expiresAt') ? '' :
      '下载授权需要接收方和明确过期时间。')
  }
  if (operation.value === 'notification') {
    return state.validate(hasRequiredString(payload, 'channel') && hasSha256(payload, 'destinationHash') &&
      hasVersionedReference(payload.payload) && hasRequiredString(payload, 'idempotencyKey') ? '' :
      '通知需要渠道、目标 SHA-256、载荷精确版本和幂等键。')
  }
  if (operation.value === 'notification-attempt') {
    const outcome = payload.outcome
    return state.validate(hasRequiredString(payload, 'idempotencyKey') && typeof outcome === 'string' &&
      ['DELIVERED', 'FAILED', 'UNKNOWN'].includes(outcome) &&
      (outcome !== 'DELIVERED' || hasRequiredString(payload, 'externalReference')) ? '' :
      '通知尝试需要幂等键和结果；DELIVERED 必须包含外部引用。')
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
    return state.validate('报告写入需要正整数 expectedCurrentVersion。')
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
    return state.validate(valid ? '' : '报告行必须固定结果、范围、追溯、认可、QC、仪器及全部上游精确版本。')
  }
  if (operation.value === 'evaluate') {
    return state.validate(hasRequiredString(payload, 'signatoryId') ? '' : '门禁评估需要签字人业务 ID。')
  }
  if (operation.value === 'submit') return state.validate('')
  if (operation.value === 'issue') {
    const valid = hasVersionedReference(payload.reauthenticationRef) &&
      hasRequiredString(payload, 'signingIntent') && hasSha256(payload, 'expectedContentHash') &&
      hasRequiredString(payload, 'signatoryId')
    return state.validate(valid ? '' : '签发需要重认证引用、签署意图、服务端待签 SHA-256 和签字人业务 ID。')
  }
  const kinds = ['CORRECTION', 'SUPPLEMENT', 'WITHDRAWAL', 'VOID', 'SUPERSESSION']
  const kind = payload.kind
  const valid = hasPositiveInteger(payload, 'versionNumber') && typeof kind === 'string' &&
    kinds.includes(kind) && hasRequiredString(payload, 'reason') &&
    (!(kind === 'CORRECTION' || kind === 'SUPPLEMENT') || hasVersionedReference(payload.impactAssessmentRef)) &&
    (kind !== 'SUPERSESSION' || hasRequiredString(payload, 'supersedingReportNumber'))
  return state.validate(valid ? '' : '受控动作需要历史版本、动作类型、原因及该动作要求的影响或替代引用。')
}

function validReportId(): boolean {
  if (lookup.reportId.trim()) return true
  state.validate('请输入 report ID。')
  return false
}

function validReportVersion(): boolean {
  if (validReportId() && positiveInteger(lookup.expectedReportVersion)) return true
  state.validate('签发门禁查询需要 report ID 和正整数精确版本。')
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
      <p class="eyebrow">LAB WORKBENCH / REPORT</p>
      <h1>报告签发、交付与通知</h1>
      <p>维护报告来源链与签发门禁，并将交付、下载授权和通知绑定到不可变报告版本。</p>
    </header>
    <LabAccessNotice :status="authStatus" :can-write="canWrite" capability="report.manage" />

    <template v-if="authenticated">
      <form class="lab-panel" @submit.prevent="submitOperation">
        <h2>执行 Report 写入</h2>
        <div class="lab-grid">
          <label>操作
            <select v-model="operation" :disabled="!canWrite || state.busy.value">
              <option value="create">创建报告草稿</option>
              <option value="line">追加报告行</option>
              <option value="evaluate">评估签发门禁</option>
              <option value="submit">提交审批</option>
              <option value="issue">受控签发</option>
              <option value="action">执行受控动作</option>
              <option value="delivery">创建版本交付</option>
              <option value="grant">创建下载授权</option>
              <option value="notification">排队交付通知</option>
              <option value="notification-attempt">记录通知尝试</option>
            </select>
          </label>
          <label v-if="['line', 'evaluate', 'submit', 'issue', 'action', 'delivery'].includes(operation)">Report ID
            <input v-model="path.reportId" required :disabled="!canWrite || state.busy.value">
          </label>
          <label v-if="operation === 'delivery'">报告版本号
            <input v-model.number="path.reportVersionNumber" type="number" min="1" step="1" required :disabled="!canWrite || state.busy.value">
          </label>
          <label v-if="operation === 'grant' || operation === 'notification'">Delivery ID
            <input v-model="path.deliveryId" required :disabled="!canWrite || state.busy.value">
          </label>
          <label v-if="operation === 'notification-attempt'">Notification ID
            <input v-model="path.notificationId" required :disabled="!canWrite || state.busy.value">
          </label>
        </div>
        <p class="lab-operation-note">客户端不生成权威哈希、签名或外部成功；成功交付必须携带服务端接受的外部证据。</p>
        <LabJsonEditor v-model="payloadText" label="请求 JSON" :disabled="!canWrite || state.busy.value" />
        <div class="lab-actions">
          <button type="submit" :disabled="!canWrite || state.busy.value">
            {{ state.busy.value ? '处理中...' : '提交写入' }}
          </button>
        </div>
      </form>

      <form class="lab-panel" @submit.prevent="loadReport">
        <h2>报告详情、版本链与交付读取</h2>
        <div class="lab-grid">
          <label>Report ID<input v-model="lookup.reportId" :disabled="state.busy.value"></label>
          <label>报告精确版本<input v-model.number="lookup.expectedReportVersion" type="number" min="1" step="1" :disabled="state.busy.value"></label>
          <label>历史版本号<input v-model.number="lookup.versionNumber" type="number" min="1" step="1" :disabled="state.busy.value"></label>
          <label>Delivery ID<input v-model="lookup.deliveryId" :disabled="state.busy.value"></label>
          <label class="wide">下载授权 token<input v-model="lookup.downloadAccessToken" :disabled="state.busy.value"></label>
        </div>
        <p v-if="state.validationError.value" class="lab-validation" role="alert">{{ state.validationError.value }}</p>
        <div class="lab-actions">
          <button type="submit" :disabled="state.busy.value">加载报告</button>
          <button type="button" class="secondary" :disabled="state.busy.value" @click="checkIssuanceGate">检查签发门禁</button>
          <button type="button" class="secondary" :disabled="state.busy.value" @click="loadPendingHash">读取待签哈希</button>
          <button type="button" class="secondary" :disabled="state.busy.value" @click="loadVerification">验证版本链</button>
          <button type="button" class="secondary" :disabled="state.busy.value" @click="loadVersion">读取历史版本</button>
          <button type="button" class="secondary" :disabled="state.busy.value" @click="loadDelivery">读取交付详情</button>
          <button type="button" class="secondary" :disabled="state.busy.value" @click="downloadVersion">下载固定版本</button>
        </div>
      </form>

      <p v-if="state.busy.value" class="lab-panel" role="status">正在等待服务端响应...</p>
      <p v-else-if="!state.response.value && !state.error.value" class="lab-panel lab-empty">尚未加载报告、版本或交付数据。</p>
      <LabProblemAlert v-if="state.error.value" :error="state.error.value" @retry="state.retryLast" />
      <LabJsonResult v-if="state.response.value" :title="state.responseTitle.value" :value="state.response.value" :blocked="blockedResponse" />
    </template>
  </main>
</template>

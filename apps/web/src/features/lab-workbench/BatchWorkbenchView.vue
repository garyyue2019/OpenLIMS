<script setup lang="ts">
import { reactive, ref } from 'vue'
import LabAccessNotice from './LabAccessNotice.vue'
import LabProblemAlert from './LabProblemAlert.vue'
import type { LabApiError } from './lab-api'
import { normalizeLabError, positiveInteger, useLabAccess } from './lab-view-state'
import {
  addBatchEvidence,
  addBatchMember,
  BATCH_RULE_SET_VERSION,
  createBatch,
  freezeBatch,
  getBatch,
  getBatchStatus,
  type AddBatchEvidenceRequest,
  type AddBatchMemberRequest,
  type BatchEvidenceResult,
  type BatchFreezeResult,
  type BatchMemberResult,
  type BatchResult,
  type BatchStatusResult,
  type CreateBatchRequest,
  type FreezeBatchRequest
} from './batch-client'

const { authStatus, authenticated, accessToken, canWrite } = useLabAccess('batch.manage')
const createForm = reactive({
  legalEntityId: '', laboratoryId: '', batchType: 'ANALYTICAL' as CreateBatchRequest['batchType']
})
const memberForm = reactive({
  batchId: '', expectedCurrentVersion: 1,
  memberType: 'SPECIMEN' as AddBatchMemberRequest['memberType'],
  customerId: '', serviceOrderId: '', productCategory: '',
  allocationId: '', expectedSubjectAllocationVersion: 1,
  qcId: '', qcVersion: 1
})
const evidenceForm = reactive({
  batchId: '', expectedCurrentVersion: 1,
  sourceSystem: 'CDS' as AddBatchEvidenceRequest['sourceSystem'],
  externalId: '', externalVersion: 1, sha256: ''
})
const freezeForm = reactive({
  batchId: '', expectedCurrentVersion: 1,
  cause: 'QC_FAILURE' as FreezeBatchRequest['cause'],
  followUpId: '', followUpVersion: 1
})
const lookup = reactive({ batchId: '', expectedVersion: 1 })
const busy = ref(false)
const validationError = ref('')
const error = ref<LabApiError>()
const batch = ref<BatchResult>()
const member = ref<BatchMemberResult>()
const evidence = ref<BatchEvidenceResult>()
const freeze = ref<BatchFreezeResult>()
const status = ref<BatchStatusResult>()
let retryAction: (() => Promise<void>) | undefined

async function createNewBatch(): Promise<void> {
  validationError.value = ''
  if (!createForm.legalEntityId.trim() || !createForm.laboratoryId.trim()) {
    validationError.value = '创建批次需要法人和实验室。'
    return
  }
  await execute(async () => {
    batch.value = await createBatch({
      ruleSetVersion: BATCH_RULE_SET_VERSION,
      objectScope: {
        legalEntityId: createForm.legalEntityId.trim(),
        laboratoryId: createForm.laboratoryId.trim()
      },
      batchType: createForm.batchType
    }, { accessToken: accessToken.value })
    setCurrentBatch(batch.value.batchId, batch.value.version)
    member.value = undefined
    evidence.value = undefined
    freeze.value = undefined
    status.value = undefined
  }, createNewBatch)
}

async function addMember(): Promise<void> {
  validationError.value = ''
  const baseValid = memberForm.batchId.trim() && positiveInteger(memberForm.expectedCurrentVersion) &&
    memberForm.customerId.trim() && memberForm.serviceOrderId.trim() && memberForm.productCategory.trim()
  const memberRefValid = memberForm.memberType === 'SPECIMEN'
    ? memberForm.allocationId.trim() && positiveInteger(memberForm.expectedSubjectAllocationVersion)
    : memberForm.qcId.trim() && positiveInteger(memberForm.qcVersion)
  if (!baseValid || !memberRefValid) {
    validationError.value = '成员需要批次精确版本和业务范围；样本成员绑定分配版本，QC 成员绑定 QC 精确版本。'
    return
  }
  const request: AddBatchMemberRequest = {
    expectedCurrentVersion: memberForm.expectedCurrentVersion,
    ruleSetVersion: BATCH_RULE_SET_VERSION,
    memberType: memberForm.memberType,
    customerId: memberForm.customerId.trim(), serviceOrderId: memberForm.serviceOrderId.trim(),
    productCategory: memberForm.productCategory.trim(),
    ...(memberForm.memberType === 'SPECIMEN'
      ? { allocationId: memberForm.allocationId.trim(), expectedSubjectAllocationVersion: memberForm.expectedSubjectAllocationVersion }
      : { qcRef: { id: memberForm.qcId.trim(), version: memberForm.qcVersion } })
  }
  await execute(async () => {
    member.value = await addBatchMember(memberForm.batchId.trim(), request, { accessToken: accessToken.value })
    setCurrentBatch(member.value.batchId, member.value.batchVersion)
    if (batch.value?.batchId === member.value.batchId) {
      batch.value.version = member.value.batchVersion
      batch.value.members.push(member.value)
    }
  }, addMember)
}

async function addEvidence(): Promise<void> {
  validationError.value = ''
  if (!evidenceForm.batchId.trim() || !positiveInteger(evidenceForm.expectedCurrentVersion) ||
      !evidenceForm.externalId.trim() || !positiveInteger(evidenceForm.externalVersion) ||
      !/^[a-fA-F0-9]{64}$/.test(evidenceForm.sha256)) {
    validationError.value = '证据需要批次精确版本、外部引用精确版本和 64 位十六进制 SHA-256。'
    return
  }
  await execute(async () => {
    evidence.value = await addBatchEvidence(evidenceForm.batchId.trim(), {
      expectedCurrentVersion: evidenceForm.expectedCurrentVersion,
      ruleSetVersion: BATCH_RULE_SET_VERSION,
      sourceSystem: evidenceForm.sourceSystem,
      externalRef: { id: evidenceForm.externalId.trim(), version: evidenceForm.externalVersion },
      sha256: evidenceForm.sha256.toLowerCase()
    }, { accessToken: accessToken.value })
    setCurrentBatch(evidence.value.batchId, evidence.value.batchVersion)
    if (batch.value?.batchId === evidence.value.batchId) {
      batch.value.version = evidence.value.batchVersion
      batch.value.evidence.push(evidence.value)
    }
  }, addEvidence)
}

async function freezeCurrentBatch(): Promise<void> {
  validationError.value = ''
  if (!freezeForm.batchId.trim() || !positiveInteger(freezeForm.expectedCurrentVersion) ||
      (freezeForm.followUpId.trim() && !positiveInteger(freezeForm.followUpVersion))) {
    validationError.value = '冻结需要批次精确版本；如填写批准后续引用，其版本必须为正整数。'
    return
  }
  await execute(async () => {
    freeze.value = await freezeBatch(freezeForm.batchId.trim(), {
      expectedCurrentVersion: freezeForm.expectedCurrentVersion,
      ruleSetVersion: BATCH_RULE_SET_VERSION,
      cause: freezeForm.cause,
      approvedFollowUpRef: freezeForm.followUpId.trim()
        ? { id: freezeForm.followUpId.trim(), version: freezeForm.followUpVersion }
        : undefined
    }, { accessToken: accessToken.value })
    setCurrentBatch(freeze.value.batchId, freeze.value.batchVersion)
    if (batch.value?.batchId === freeze.value.batchId) {
      batch.value.version = freeze.value.batchVersion
      batch.value.state = 'FROZEN'
      batch.value.freeze = freeze.value
    }
  }, freezeCurrentBatch)
}

async function loadBatch(): Promise<void> {
  validationError.value = ''
  if (!lookup.batchId.trim()) {
    validationError.value = '请输入批次 ID。'
    return
  }
  await execute(async () => {
    batch.value = await getBatch(lookup.batchId.trim(), { accessToken: accessToken.value })
    setCurrentBatch(batch.value.batchId, batch.value.version)
  }, loadBatch)
}

async function checkBatchStatus(): Promise<void> {
  validationError.value = ''
  if (!lookup.batchId.trim() || !positiveInteger(lookup.expectedVersion)) {
    validationError.value = '状态检查需要批次 ID 和正整数精确版本。'
    return
  }
  await execute(async () => {
    status.value = await getBatchStatus(
      lookup.batchId.trim(), lookup.expectedVersion, { accessToken: accessToken.value }
    )
  }, checkBatchStatus)
}

function setCurrentBatch(batchId: string, version: number): void {
  lookup.batchId = batchId
  lookup.expectedVersion = version
  memberForm.batchId = batchId
  memberForm.expectedCurrentVersion = version
  evidenceForm.batchId = batchId
  evidenceForm.expectedCurrentVersion = version
  freezeForm.batchId = batchId
  freezeForm.expectedCurrentVersion = version
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
      <p class="eyebrow">LAB WORKBENCH · BATCH · {{ BATCH_RULE_SET_VERSION }}</p>
      <h1>批次管理</h1>
      <p>创建类型化批次，按当前版本追加成员和证据，并以受控原因冻结。</p>
    </header>
    <LabAccessNotice :status="authStatus" :can-write="canWrite" capability="batch.manage" />

    <template v-if="authenticated">
      <form class="lab-panel" @submit.prevent="createNewBatch">
        <h2>创建批次</h2>
        <div class="lab-grid">
          <label>法人<input v-model="createForm.legalEntityId" required :disabled="!canWrite || busy"></label>
          <label>实验室<input v-model="createForm.laboratoryId" required :disabled="!canWrite || busy"></label>
          <label>批次类型<select v-model="createForm.batchType" :disabled="!canWrite || busy"><option>PREPARATION</option><option>PRECONDITIONING</option><option>ANALYTICAL</option><option>INSTRUMENT_RUN</option></select></label>
        </div>
        <div class="lab-actions"><button type="submit" :disabled="!canWrite || busy">创建批次</button></div>
      </form>

      <form class="lab-panel" @submit.prevent="addMember">
        <h2>追加成员</h2>
        <div class="lab-grid">
          <label>批次 ID<input v-model="memberForm.batchId" required :disabled="!canWrite || busy"></label>
          <label>当前精确版本<input v-model.number="memberForm.expectedCurrentVersion" type="number" min="1" step="1" required :disabled="!canWrite || busy"></label>
          <label>成员类型<select v-model="memberForm.memberType" :disabled="!canWrite || busy"><option>SPECIMEN</option><option>QC_SAMPLE</option></select></label>
          <label>客户<input v-model="memberForm.customerId" required :disabled="!canWrite || busy"></label>
          <label>服务委托<input v-model="memberForm.serviceOrderId" required :disabled="!canWrite || busy"></label>
          <label>产品类别<input v-model="memberForm.productCategory" required :disabled="!canWrite || busy"></label>
          <template v-if="memberForm.memberType === 'SPECIMEN'">
            <label>分配 ID<input v-model="memberForm.allocationId" required :disabled="!canWrite || busy"></label>
            <label>主体分配版本<input v-model.number="memberForm.expectedSubjectAllocationVersion" type="number" min="1" step="1" required :disabled="!canWrite || busy"></label>
          </template>
          <template v-else>
            <label>QC 引用 ID<input v-model="memberForm.qcId" required :disabled="!canWrite || busy"></label>
            <label>QC 引用版本<input v-model.number="memberForm.qcVersion" type="number" min="1" step="1" required :disabled="!canWrite || busy"></label>
          </template>
        </div>
        <div class="lab-actions"><button type="submit" :disabled="!canWrite || busy">追加成员</button></div>
      </form>

      <form class="lab-panel" @submit.prevent="addEvidence">
        <h2>追加外部证据</h2>
        <div class="lab-grid">
          <label>批次 ID<input v-model="evidenceForm.batchId" required :disabled="!canWrite || busy"></label>
          <label>当前精确版本<input v-model.number="evidenceForm.expectedCurrentVersion" type="number" min="1" step="1" required :disabled="!canWrite || busy"></label>
          <label>来源系统<select v-model="evidenceForm.sourceSystem" :disabled="!canWrite || busy"><option>CDS</option><option>ELN</option><option>INSTRUMENT</option></select></label>
          <label>外部引用 ID<input v-model="evidenceForm.externalId" required :disabled="!canWrite || busy"></label>
          <label>外部引用版本<input v-model.number="evidenceForm.externalVersion" type="number" min="1" step="1" required :disabled="!canWrite || busy"></label>
          <label class="wide">SHA-256<input v-model="evidenceForm.sha256" minlength="64" maxlength="64" required :disabled="!canWrite || busy"></label>
        </div>
        <div class="lab-actions"><button type="submit" :disabled="!canWrite || busy">追加证据</button></div>
      </form>

      <form class="lab-panel" @submit.prevent="freezeCurrentBatch">
        <h2>冻结批次</h2>
        <div class="lab-grid">
          <label>批次 ID<input v-model="freezeForm.batchId" required :disabled="!canWrite || busy"></label>
          <label>当前精确版本<input v-model.number="freezeForm.expectedCurrentVersion" type="number" min="1" step="1" required :disabled="!canWrite || busy"></label>
          <label>冻结原因<select v-model="freezeForm.cause" :disabled="!canWrite || busy"><option>QC_FAILURE</option><option>ENVIRONMENT_OUT_OF_TOLERANCE</option><option>CALIBRATION_INVALID</option></select></label>
          <label>批准后续引用 ID<input v-model="freezeForm.followUpId" :disabled="!canWrite || busy"></label>
          <label>批准后续引用版本<input v-model.number="freezeForm.followUpVersion" type="number" min="1" step="1" :disabled="!canWrite || busy"></label>
        </div>
        <div class="lab-actions"><button type="submit" :disabled="!canWrite || busy">冻结批次</button></div>
      </form>

      <form class="lab-panel" @submit.prevent="loadBatch">
        <h2>批次详情与状态</h2>
        <div class="lab-grid">
          <label>批次 ID<input v-model="lookup.batchId" required :disabled="busy"></label>
          <label>精确版本<input v-model.number="lookup.expectedVersion" type="number" min="1" step="1" required :disabled="busy"></label>
        </div>
        <p v-if="validationError" class="lab-validation" role="alert">{{ validationError }}</p>
        <div class="lab-actions">
          <button type="submit" :disabled="busy">加载批次</button>
          <button type="button" class="secondary" :disabled="busy" @click="checkBatchStatus">检查状态</button>
        </div>
      </form>

      <LabProblemAlert v-if="error" :error="error" @retry="retryLast" />

      <section v-if="batch" class="lab-panel lab-result" aria-live="polite">
        <h2>批次详情</h2>
        <dl class="lab-details">
          <div><dt>批次 ID</dt><dd>{{ batch.batchId }}</dd></div>
          <div><dt>类型</dt><dd>{{ batch.batchType }}</dd></div>
          <div><dt>状态</dt><dd>{{ batch.state }}</dd></div>
          <div><dt>精确版本</dt><dd>{{ batch.version }}</dd></div>
          <div><dt>成员数</dt><dd>{{ batch.members.length }}</dd></div>
          <div><dt>证据数</dt><dd>{{ batch.evidence.length }}</dd></div>
        </dl>
        <h3>成员</h3><ul><li v-for="item in batch.members" :key="item.memberId">{{ item.memberId }} · {{ item.memberType }} · 版本 {{ item.batchVersion }}</li><li v-if="!batch.members.length">尚无成员</li></ul>
        <h3>证据</h3><ul><li v-for="item in batch.evidence" :key="item.evidenceId">{{ item.evidenceId }} · {{ item.sourceSystem }} · {{ item.sha256 }}</li><li v-if="!batch.evidence.length">尚无证据</li></ul>
        <p v-if="batch.freeze"><strong>冻结：</strong>{{ batch.freeze.cause }} · 影响 {{ batch.freeze.affectedMemberCount }} 个成员</p>
      </section>

      <section v-if="member" class="lab-panel lab-result" aria-live="polite"><h2>成员已追加</h2><p>{{ member.memberId }} · 批次版本 {{ member.batchVersion }}</p></section>
      <section v-if="evidence" class="lab-panel lab-result" aria-live="polite"><h2>证据已追加</h2><p>{{ evidence.evidenceId }} · 批次版本 {{ evidence.batchVersion }}</p></section>
      <section v-if="freeze" class="lab-panel lab-blocked" aria-live="polite"><h2>批次已冻结</h2><p>{{ freeze.cause }} · 批次版本 {{ freeze.batchVersion }}</p></section>
      <section v-if="status" class="lab-panel" :class="status.decision === 'ALLOWED' ? 'lab-result' : 'lab-blocked'" aria-live="polite"><h2>批次状态决定：{{ status.decision }}</h2><p>业务状态：{{ status.state ?? '不可用' }} · 当前版本：{{ status.currentBatchVersion ?? '不可用' }}</p><p>原因码：{{ status.reasonCodes.join('、') || '无' }}</p></section>
    </template>
  </main>
</template>

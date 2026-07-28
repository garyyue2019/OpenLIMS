<script setup lang="ts">
import { reactive, ref } from 'vue'
import LabAccessNotice from './LabAccessNotice.vue'
import LabProblemAlert from './LabProblemAlert.vue'
import { type LabApiError } from './lab-api'
import { normalizeLabError, nonNegativeInteger, positiveInteger, useLabAccess } from './lab-view-state'
import {
  createScopeMatrix,
  getScopeMatrixVersion,
  getScopeProductionEligibility,
  reviseScopeMatrix,
  SCOPE_RULE_SET_VERSION,
  type ScopeLineInput,
  type ScopeMatrixVersionResult,
  type ScopeProductionEligibilityResult,
  type ScopeVersionedReference,
  type SubmitScopeMatrixVersionRequest
} from './scope-client'

interface EditableScopeLine {
  subjectType: ScopeLineInput['subjectType']
  subjectId: string
  subjectVersion: number
  targetMarketId: string
  targetMarketVersion: number
  requirementClauseId: string
  requirementClauseVersion: number
  testItemId: string
  testItemVersion: number
  methodId: string
  methodVersion: number
  methodOption: string
  sampleRequirementId: string
  sampleRequirementVersion: number
  evaluationMode: ScopeLineInput['evaluationMode']
  workCenterId: string
  workCenterVersion: number
  reportPosition: string
}

const { authStatus, authenticated, accessToken, canWrite } = useLabAccess('scope.approve')
const mode = ref<'create' | 'revise'>('create')
const matrixId = ref('')
const expectedCurrentVersion = ref(0)
const objectScope = reactive({
  legalEntityId: '', laboratoryId: '', customerId: '', serviceOrderId: '', productCategory: ''
})
const lines = reactive<EditableScopeLine[]>([newLine()])
const lookup = reactive({ matrixId: '', version: 1 })
const busy = ref(false)
const validationError = ref('')
const error = ref<LabApiError>()
const result = ref<ScopeMatrixVersionResult>()
const eligibility = ref<ScopeProductionEligibilityResult>()
let retryAction: (() => Promise<void>) | undefined

function newLine(): EditableScopeLine {
  return {
    subjectType: 'FEATURE_NODE', subjectId: '', subjectVersion: 1,
    targetMarketId: '', targetMarketVersion: 1,
    requirementClauseId: '', requirementClauseVersion: 1,
    testItemId: '', testItemVersion: 1,
    methodId: '', methodVersion: 1, methodOption: '',
    sampleRequirementId: '', sampleRequirementVersion: 1,
    evaluationMode: 'EVALUATED', workCenterId: '', workCenterVersion: 1,
    reportPosition: ''
  }
}

function addLine(): void { lines.push(newLine()) }
function removeLine(index: number): void { if (lines.length > 1) lines.splice(index, 1) }

async function submitScope(): Promise<void> {
  if (!canWrite.value || busy.value) return
  const request = buildRequest()
  if (!request) return
  await execute(async () => {
    const context = { accessToken: accessToken.value }
    result.value = mode.value === 'create'
      ? await createScopeMatrix(request, context)
      : await reviseScopeMatrix(matrixId.value.trim(), request, context)
    eligibility.value = undefined
    lookup.matrixId = result.value.scopeMatrixId
    lookup.version = result.value.version
    matrixId.value = result.value.scopeMatrixId
    expectedCurrentVersion.value = result.value.version
    mode.value = 'revise'
  }, submitScope)
}

async function loadScope(): Promise<void> {
  validationError.value = ''
  if (!lookup.matrixId.trim() || !positiveInteger(lookup.version)) {
    validationError.value = '查询需要范围矩阵 ID 和大于 0 的精确版本。'
    return
  }
  await execute(async () => {
    result.value = await getScopeMatrixVersion(
      lookup.matrixId.trim(), lookup.version, { accessToken: accessToken.value }
    )
    matrixId.value = result.value.scopeMatrixId
    expectedCurrentVersion.value = result.value.version
    mode.value = 'revise'
  }, loadScope)
}

async function checkEligibility(): Promise<void> {
  validationError.value = ''
  const id = result.value?.scopeMatrixId ?? lookup.matrixId.trim()
  const version = result.value?.version ?? lookup.version
  if (!id || !positiveInteger(version)) {
    validationError.value = '资格检查需要已加载的范围矩阵及其精确版本。'
    return
  }
  await execute(async () => {
    eligibility.value = await getScopeProductionEligibility(
      id, version, { accessToken: accessToken.value }
    )
  }, checkEligibility)
}

function buildRequest(): SubmitScopeMatrixVersionRequest | undefined {
  validationError.value = ''
  const requiredScope = Object.values(objectScope).every(value => value.trim())
  const expectedVersionValid = mode.value === 'create'
    ? expectedCurrentVersion.value === 0
    : Boolean(matrixId.value.trim()) && positiveInteger(expectedCurrentVersion.value)
  const linesValid = lines.length > 0 && lines.every(line =>
    [line.subjectId, line.targetMarketId, line.requirementClauseId, line.testItemId,
      line.methodId, line.methodOption, line.sampleRequirementId, line.workCenterId,
      line.reportPosition].every(value => value.trim()) &&
    [line.subjectVersion, line.targetMarketVersion, line.requirementClauseVersion,
      line.testItemVersion, line.methodVersion, line.sampleRequirementVersion,
      line.workCenterVersion].every(positiveInteger)
  )
  if (!requiredScope || !expectedVersionValid || !linesValid ||
      !nonNegativeInteger(expectedCurrentVersion.value)) {
    validationError.value = '请填写全部范围字段；引用版本必须是正整数，创建版本固定为 0，修订版本必须大于 0。'
    return undefined
  }
  return {
    expectedCurrentVersion: expectedCurrentVersion.value,
    ruleSetVersion: SCOPE_RULE_SET_VERSION,
    objectScope: { ...objectScope },
    lines: lines.map(toScopeLine)
  }
}

function toScopeLine(line: EditableScopeLine): ScopeLineInput {
  const versioned = (id: string, version: number): ScopeVersionedReference => ({ id: id.trim(), version })
  return {
    subjectType: line.subjectType,
    subject: versioned(line.subjectId, line.subjectVersion),
    targetMarket: versioned(line.targetMarketId, line.targetMarketVersion),
    requirementClause: versioned(line.requirementClauseId, line.requirementClauseVersion),
    testItem: versioned(line.testItemId, line.testItemVersion),
    method: versioned(line.methodId, line.methodVersion),
    methodOption: line.methodOption.trim(),
    sampleRequirement: versioned(line.sampleRequirementId, line.sampleRequirementVersion),
    evaluationMode: line.evaluationMode,
    workCenter: versioned(line.workCenterId, line.workCenterVersion),
    reportPosition: line.reportPosition.trim()
  }
}

async function execute(action: () => Promise<void>, retry: () => Promise<void>): Promise<void> {
  if (!authenticated.value || !accessToken.value || busy.value) return
  busy.value = true
  error.value = undefined
  retryAction = retry
  try { await action() } catch (caught) { error.value = normalizeLabError(caught) } finally { busy.value = false }
}

function retry(): void { if (retryAction && !busy.value) void retryAction() }
</script>

<template>
  <main class="lab-workbench-page">
    <header class="lab-workbench-heading">
      <p class="eyebrow">LAB WORKBENCH · SCOPE · {{ SCOPE_RULE_SET_VERSION }}</p>
      <h1>范围矩阵</h1>
      <p>创建或修订不可变范围版本，按精确版本恢复详情并检查生产资格。</p>
    </header>

    <LabAccessNotice :status="authStatus" :can-write="canWrite" capability="scope.approve" />

    <template v-if="authenticated">
      <form class="lab-panel" @submit.prevent="submitScope">
        <h2>创建或修订范围版本</h2>
        <div class="lab-grid">
          <label>操作
            <select v-model="mode" :disabled="!canWrite || busy" @change="expectedCurrentVersion = mode === 'create' ? 0 : Math.max(1, expectedCurrentVersion)">
              <option value="create">创建矩阵</option><option value="revise">追加修订</option>
            </select>
          </label>
          <label>范围矩阵 ID（修订必填）<input v-model="matrixId" :disabled="!canWrite || busy || mode === 'create'"></label>
          <label>当前精确版本<input v-model.number="expectedCurrentVersion" type="number" min="0" step="1" required :disabled="!canWrite || busy"></label>
          <label>法人<input v-model="objectScope.legalEntityId" required :disabled="!canWrite || busy"></label>
          <label>实验室<input v-model="objectScope.laboratoryId" required :disabled="!canWrite || busy"></label>
          <label>客户<input v-model="objectScope.customerId" required :disabled="!canWrite || busy"></label>
          <label>服务委托<input v-model="objectScope.serviceOrderId" required :disabled="!canWrite || busy"></label>
          <label>产品类别<input v-model="objectScope.productCategory" required :disabled="!canWrite || busy"></label>
        </div>

        <fieldset v-for="(line, index) in lines" :key="index" class="lab-line">
          <legend>范围行 {{ index + 1 }}</legend>
          <div class="lab-grid">
            <label>主体类型<select v-model="line.subjectType" :disabled="!canWrite || busy"><option>SUBMISSION_ITEM</option><option>PRODUCT_VARIANT</option><option>FEATURE_NODE</option></select></label>
            <label>主体 ID<input v-model="line.subjectId" required :disabled="!canWrite || busy"></label>
            <label>主体版本<input v-model.number="line.subjectVersion" type="number" min="1" step="1" required :disabled="!canWrite || busy"></label>
            <label>目标市场 ID<input v-model="line.targetMarketId" required :disabled="!canWrite || busy"></label>
            <label>目标市场版本<input v-model.number="line.targetMarketVersion" type="number" min="1" step="1" required :disabled="!canWrite || busy"></label>
            <label>要求条款 ID<input v-model="line.requirementClauseId" required :disabled="!canWrite || busy"></label>
            <label>要求条款版本<input v-model.number="line.requirementClauseVersion" type="number" min="1" step="1" required :disabled="!canWrite || busy"></label>
            <label>检测项目 ID<input v-model="line.testItemId" required :disabled="!canWrite || busy"></label>
            <label>检测项目版本<input v-model.number="line.testItemVersion" type="number" min="1" step="1" required :disabled="!canWrite || busy"></label>
            <label>方法 ID<input v-model="line.methodId" required :disabled="!canWrite || busy"></label>
            <label>方法版本<input v-model.number="line.methodVersion" type="number" min="1" step="1" required :disabled="!canWrite || busy"></label>
            <label>方法选项<input v-model="line.methodOption" required :disabled="!canWrite || busy"></label>
            <label>样品要求 ID<input v-model="line.sampleRequirementId" required :disabled="!canWrite || busy"></label>
            <label>样品要求版本<input v-model.number="line.sampleRequirementVersion" type="number" min="1" step="1" required :disabled="!canWrite || busy"></label>
            <label>评价模式<select v-model="line.evaluationMode" :disabled="!canWrite || busy"><option>MEASURED_ONLY</option><option>EVALUATED</option><option>NOT_EVALUATED</option><option>WAIVED</option></select></label>
            <label>工作中心 ID<input v-model="line.workCenterId" required :disabled="!canWrite || busy"></label>
            <label>工作中心版本<input v-model.number="line.workCenterVersion" type="number" min="1" step="1" required :disabled="!canWrite || busy"></label>
            <label>报告位置<input v-model="line.reportPosition" required :disabled="!canWrite || busy"></label>
          </div>
          <div class="lab-actions"><button type="button" class="secondary" :disabled="!canWrite || busy || lines.length === 1" @click="removeLine(index)">删除此行</button></div>
        </fieldset>
        <p v-if="validationError" class="lab-validation" role="alert">{{ validationError }}</p>
        <div class="lab-actions">
          <button type="button" class="secondary" :disabled="!canWrite || busy" @click="addLine">添加范围行</button>
          <button type="submit" :disabled="!canWrite || busy">{{ busy ? '提交中…' : mode === 'create' ? '创建并批准' : '追加批准版本' }}</button>
        </div>
      </form>

      <form class="lab-panel" @submit.prevent="loadScope">
        <h2>按精确版本查询</h2>
        <div class="lab-grid">
          <label>范围矩阵 ID<input v-model="lookup.matrixId" required :disabled="busy"></label>
          <label>版本<input v-model.number="lookup.version" type="number" min="1" step="1" required :disabled="busy"></label>
        </div>
        <div class="lab-actions">
          <button type="submit" :disabled="busy">加载详情</button>
          <button type="button" class="secondary" :disabled="busy" @click="checkEligibility">检查生产资格</button>
        </div>
      </form>

      <LabProblemAlert v-if="error" :error="error" @retry="retry" />

      <section v-if="result" class="lab-panel lab-result" aria-live="polite">
        <h2>范围版本详情</h2>
        <dl class="lab-details">
          <div><dt>矩阵 ID</dt><dd>{{ result.scopeMatrixId }}</dd></div>
          <div><dt>精确版本</dt><dd>{{ result.version }}</dd></div>
          <div><dt>状态</dt><dd>{{ result.state }}</dd></div>
          <div><dt>规则集</dt><dd>{{ result.ruleSetVersion }}</dd></div>
          <div><dt>批准人</dt><dd>{{ result.approvedBy }}</dd></div>
          <div><dt>批准时间</dt><dd>{{ result.approvedAt }}</dd></div>
        </dl>
        <ol><li v-for="line in result.lines" :key="line.scopeLineId">{{ line.scopeLineId }} · {{ line.subjectType }} · {{ line.testItem.id }}@{{ line.testItem.version }} · {{ line.evaluationMode }}</li></ol>
      </section>

      <section v-if="eligibility" class="lab-panel" :class="eligibility.decision === 'ALLOWED' ? 'lab-result' : 'lab-blocked'" aria-live="polite">
        <h2>生产资格：{{ eligibility.decision }}</h2>
        <p>矩阵版本：{{ eligibility.currentMatrixVersion ?? '不可用' }}</p>
        <p>原因码：{{ eligibility.reasonCodes.length ? eligibility.reasonCodes.join('、') : '无' }}</p>
      </section>
    </template>
  </main>
</template>

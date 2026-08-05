<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'
import { authSnapshot } from '../../auth-store'
import LabAccessNotice from '../lab-workbench/LabAccessNotice.vue'
import LabJsonEditor from '../lab-workbench/LabJsonEditor.vue'
import LabJsonResult from '../lab-workbench/LabJsonResult.vue'
import LabProblemAlert from '../lab-workbench/LabProblemAlert.vue'
import {
  hasArray, hasPositiveInteger, hasRequiredString, hasSha256, hasVersionedReference,
  isJsonRecord, parseJsonObject, prettyJson, type JsonRecord
} from '../lab-workbench/lab-json'
import { useLabOperationState } from '../lab-workbench/lab-operation-state'
import { hasLabCapability } from '../lab-workbench/lab-workbench-access'
import {
  createToyItemConclusion, createToyScopeConclusion, getToyConclusion,
  getToyConclusionsByProduct, TOY_CONCLUSION_RULE_SET_VERSION,
  type CreateToyItemConclusionRequest, type CreateToyScopeConclusionRequest
} from './toy-conclusion-client'

type ConclusionOperation = 'item' | 'scope'
const samples: Record<ConclusionOperation, JsonRecord> = {
  item: {
    ruleSetVersion: TOY_CONCLUSION_RULE_SET_VERSION,
    adoptedResultRef: 'adopted-result-id', adoptedResultVersion: 1,
    requirementRef: 'requirement-id', requirementVersion: 1
  },
  scope: {
    ruleSetVersion: TOY_CONCLUSION_RULE_SET_VERSION,
    productRef: 'product-id', productVersion: 1,
    testUnitPlanRef: 'test-unit-plan-id', testUnitPlanVersion: 1,
    testUnits: [{
      testUnitId: 'TEST-UNIT-1', physicalObjectRef: 'received-item-id', physicalObjectVersion: 1,
      hazardDomainRef: 'MECHANICAL', hazardDomainVersion: 1,
      adoptedResultRef: 'adopted-result-id', adoptedResultVersion: 1,
      resultProvenanceGraphRef: 'provenance-graph-id', resultProvenanceGraphVersion: 1,
      coverageDecisionRef: 'coverage-decision-id', coverageDecisionVersion: 1,
      requirementRefs: ['requirement-id']
    }],
    uncoveredScopes: [{ scope: 'CHEMICAL', reason: 'NOT_TESTED', detail: 'Not included in this tested scope.' }],
    externalReferences: [{ issuer: 'Customer', reference: 'DECL-1', statedScope: 'Declared use', notPartOfThisConclusion: true }],
    isFictitiousWholeItemConclusion: false,
    reauthenticationRef: { id: 'reauthentication-id', version: 1 },
    signingIntent: 'Approve the tested-scope conformity statement.',
    signedContentHash: 'a'.repeat(64)
  }
}

const operation = ref<ConclusionOperation>('item')
const payloadText = ref(prettyJson(samples.item))
const lookup = reactive({ mode: 'id' as 'id' | 'product', conclusionId: '', productRef: '', productVersion: 1 })
const authStatus = computed(() => authSnapshot.value.status)
const authenticated = computed(() => authSnapshot.value.status === 'authenticated')
const accessToken = computed(() => authSnapshot.value.user?.access_token ?? '')
const profile = computed(() => authSnapshot.value.user?.profile as Readonly<Record<string, unknown>> | undefined)
const canApproveItem = computed(() => authenticated.value && hasLabCapability(profile.value, 'toy.conclusion.approve-item'))
const canApproveScope = computed(() => authenticated.value && hasLabCapability(profile.value, 'toy.conclusion.approve-scope'))
const canOperate = computed(() => operation.value === 'item' ? canApproveItem.value : canApproveScope.value)
const canRead = computed(() => canApproveItem.value || canApproveScope.value)
const state = useLabOperationState(authenticated, accessToken)

watch(operation, value => { payloadText.value = prettyJson(samples[value]) })

async function submitConclusion(): Promise<void> {
  const payload = readPayload()
  if (!payload || !validatePayload(payload) || !canOperate.value) return
  const result = await state.execute(
    operation.value === 'item' ? 'ITEM_CONFORMITY 结论' : 'TESTED_SCOPE_CONFORMITY 结论',
    () => operation.value === 'item'
      ? createToyItemConclusion(payload as unknown as CreateToyItemConclusionRequest, context())
      : createToyScopeConclusion(payload as unknown as CreateToyScopeConclusionRequest, context()),
    submitConclusion
  )
  if (result) {
    lookup.mode = 'id'
    lookup.conclusionId = result.conclusionId
  }
}

async function loadConclusions(): Promise<void> {
  if (!canRead.value) {
    state.validate('结论查询至少需要一种 Toy 结论批准能力。')
    return
  }
  if (lookup.mode === 'id') {
    if (!lookup.conclusionId.trim()) {
      state.validate('请输入 conclusion ID。')
      return
    }
    await state.execute(
      'Toy 结论详情', () => getToyConclusion(lookup.conclusionId.trim(), context()), loadConclusions
    )
    return
  }
  if (!lookup.productRef.trim() || !positiveInteger(lookup.productVersion)) {
    state.validate('按产品查询需要 product ref 和正整数 product version。')
    return
  }
  await state.execute(
    'Toy 产品结论列表',
    () => getToyConclusionsByProduct(lookup.productRef.trim(), lookup.productVersion, context()),
    loadConclusions
  )
}

function readPayload(): JsonRecord | undefined {
  try { return parseJsonObject(payloadText.value) } catch (error) {
    state.validate(error instanceof Error ? error.message : '请求 JSON 无效。')
    return undefined
  }
}

function validatePayload(payload: JsonRecord): boolean {
  if (payload.ruleSetVersion !== TOY_CONCLUSION_RULE_SET_VERSION) {
    return state.validate(`规则集必须固定为 ${TOY_CONCLUSION_RULE_SET_VERSION}。`)
  }
  if (Object.prototype.hasOwnProperty.call(payload, 'customStatement')) {
    return state.validate('结论措辞由系统固定生成，禁止提交 customStatement。')
  }
  if (operation.value === 'item') {
    const valid = hasRequiredString(payload, 'adoptedResultRef') &&
      hasPositiveInteger(payload, 'adoptedResultVersion') && hasRequiredString(payload, 'requirementRef') &&
      hasPositiveInteger(payload, 'requirementVersion')
    return state.validate(valid ? '' : '项目结论需要采用结果和要求的精确引用及版本。')
  }
  if (payload.isFictitiousWholeItemConclusion === true) {
    return state.validate('禁止虚构整件全面合规结论。')
  }
  const valid = hasRequiredString(payload, 'productRef') && hasPositiveInteger(payload, 'productVersion') &&
    hasRequiredString(payload, 'testUnitPlanRef') && hasPositiveInteger(payload, 'testUnitPlanVersion') &&
    hasArray(payload, 'testUnits') && (payload.testUnits as unknown[]).every(validTestUnit) &&
    hasArray(payload, 'uncoveredScopes') && (payload.uncoveredScopes as unknown[]).every(validUncoveredScope) &&
    validateExternalReferences(payload.externalReferences) && hasVersionedReference(payload.reauthenticationRef) &&
    hasRequiredString(payload, 'signingIntent') && hasSha256(payload, 'signedContentHash')
  return state.validate(valid ? '' : '范围结论需要完整 TestUnit/覆盖证据、未覆盖项、重认证、签署意图和 SHA-256 内容哈希。')
}

function validTestUnit(value: unknown): boolean {
  if (!isJsonRecord(value)) return false
  const versions = [
    'physicalObjectVersion', 'hazardDomainVersion', 'adoptedResultVersion',
    'resultProvenanceGraphVersion', 'coverageDecisionVersion'
  ]
  const strings = [
    'testUnitId', 'physicalObjectRef', 'hazardDomainRef', 'adoptedResultRef',
    'resultProvenanceGraphRef', 'coverageDecisionRef'
  ]
  const requirementsValid = value.requirementRefs === undefined ||
    (Array.isArray(value.requirementRefs) && value.requirementRefs.every(item => typeof item === 'string' && item.trim()))
  return versions.every(key => hasPositiveInteger(value, key)) &&
    strings.every(key => hasRequiredString(value, key)) && requirementsValid
}

function validUncoveredScope(value: unknown): boolean {
  return isJsonRecord(value) && hasRequiredString(value, 'scope') && hasRequiredString(value, 'detail') &&
    typeof value.reason === 'string' && ['NOT_TESTED', 'UNKNOWN', 'NOT_APPLICABLE'].includes(value.reason)
}

function validateExternalReferences(value: unknown): boolean {
  return value === undefined || (Array.isArray(value) && value.every(item =>
    isJsonRecord(item) && hasRequiredString(item, 'issuer') && hasRequiredString(item, 'reference') &&
    hasRequiredString(item, 'statedScope') && item.notPartOfThisConclusion === true))
}

function positiveInteger(value: number): boolean { return Number.isInteger(value) && value > 0 }
function context() { return { accessToken: accessToken.value } }
</script>

<template>
  <main class="lab-workbench-page">
    <header class="lab-workbench-heading">
      <p class="eyebrow">TOY WORKBENCH · CONCLUSIONS · {{ TOY_CONCLUSION_RULE_SET_VERSION }}</p>
      <h1>玩具两级符合性结论</h1>
      <p>仅创建项目符合性或已测范围符合性，结论措辞、签署绑定和职责分离均由服务器控制。</p>
    </header>
    <LabAccessNotice :status="authStatus" :can-write="canApproveItem || canApproveScope" capability="toy.conclusion.approve-item / toy.conclusion.approve-scope" />

    <template v-if="authenticated">
      <form class="lab-panel" @submit.prevent="submitConclusion">
        <h2>创建固定结论</h2>
        <div class="lab-grid"><label>结论层级<select v-model="operation" :disabled="state.busy.value"><option value="item">ITEM_CONFORMITY</option><option value="scope">TESTED_SCOPE_CONFORMITY</option></select></label></div>
        <p v-if="!canOperate" class="lab-validation" role="status">当前身份缺少 {{ operation === 'item' ? 'toy.conclusion.approve-item' : 'toy.conclusion.approve-scope' }}。</p>
        <p class="lab-operation-note">不存在整件全面合规层级；禁止自选措辞。范围结论必须披露未覆盖项，并绑定外部完成的重认证与签署哈希。</p>
        <LabJsonEditor v-model="payloadText" label="请求 JSON" :disabled="!canOperate || state.busy.value" />
        <div class="lab-actions"><button type="submit" :disabled="!canOperate || state.busy.value">创建结论</button></div>
      </form>

      <form class="lab-panel" @submit.prevent="loadConclusions">
        <h2>查询服务端结论</h2>
        <div class="lab-grid">
          <label>查询方式<select v-model="lookup.mode" :disabled="!canRead || state.busy.value"><option value="id">按 Conclusion ID</option><option value="product">按产品精确版本</option></select></label>
          <label v-if="lookup.mode === 'id'">Conclusion ID<input v-model="lookup.conclusionId" required :disabled="!canRead || state.busy.value"></label>
          <label v-if="lookup.mode === 'product'">Product ref<input v-model="lookup.productRef" required :disabled="!canRead || state.busy.value"></label>
          <label v-if="lookup.mode === 'product'">Product version<input v-model.number="lookup.productVersion" type="number" min="1" step="1" required :disabled="!canRead || state.busy.value"></label>
        </div>
        <p v-if="state.validationError.value" class="lab-validation" role="alert">{{ state.validationError.value }}</p>
        <div class="lab-actions"><button type="submit" :disabled="!canRead || state.busy.value">查询结论</button></div>
      </form>
      <LabProblemAlert v-if="state.error.value" :error="state.error.value" @retry="state.retryLast" />
      <LabJsonResult v-if="state.response.value" :title="state.responseTitle.value" :value="state.response.value" />
    </template>
  </main>
</template>

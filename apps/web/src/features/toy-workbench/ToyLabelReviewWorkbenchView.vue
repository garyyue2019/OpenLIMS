<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'
import { authSnapshot } from '../../auth-store'
import LabAccessNotice from '../lab-workbench/LabAccessNotice.vue'
import LabJsonEditor from '../lab-workbench/LabJsonEditor.vue'
import LabJsonResult from '../lab-workbench/LabJsonResult.vue'
import LabProblemAlert from '../lab-workbench/LabProblemAlert.vue'
import {
  hasArray, hasNonNegativeInteger, hasPositiveInteger, hasRequiredString, hasSha256,
  hasVersionedReference, isJsonRecord, parseJsonObject, prettyJson, type JsonRecord
} from '../lab-workbench/lab-json'
import { useLabOperationState } from '../lab-workbench/lab-operation-state'
import { hasLabCapability } from '../lab-workbench/lab-workbench-access'
import {
  appendToyLabelArtifactVersion, createToyLabelArtifact, createToyLabelReview,
  decideToyLabelReview, getToyLabelReviewStatus, TOY_LABEL_REVIEW_RULE_SET_VERSION,
  type AppendToyLabelArtifactVersionRequest, type CreateToyLabelArtifactRequest,
  type CreateToyLabelReviewRequest, type DecideToyLabelReviewRequest,
  type ToyLabelReviewStatusQuery
} from './toy-label-review-client'

type LabelOperation = 'artifact' | 'version' | 'review' | 'decision'
type ArtifactType = CreateToyLabelArtifactRequest['artifactType']
const hash = 'a'.repeat(64)
const versioned = (id: string) => ({ id, version: 1 })
const evidence = [{ objectRef: { bucket: 'toy-label-evidence', objectKey: 'labels/product-1.png' }, hash }]
const samples: Record<LabelOperation, JsonRecord> = {
  artifact: {
    objectScope: { legalEntityId: 'legal-entity-id', laboratoryId: 'laboratory-id' },
    expectedCurrentVersion: 0, artifactType: 'LABEL', language: 'zh-CN', market: 'CN',
    contentHash: hash, imageEvidenceRefs: evidence
  },
  version: { expectedCurrentVersion: 1, contentHash: hash, imageEvidenceRefs: evidence },
  review: {
    expectedCurrentVersion: 0, artifactVersion: 1, productVersion: 1,
    ageGradeDecisionVersion: 1, market: 'CN', language: 'zh-CN',
    reviewScopeRefs: [versioned('label-review-scope-id')],
    impactRuleRef: versioned('toy-label-impact-rule'),
    ruleSetVersion: TOY_LABEL_REVIEW_RULE_SET_VERSION
  },
  decision: { expectedCurrentVersion: 1, decision: 'APPROVED', decisionReason: 'Evidence reviewed.' }
}

const operation = ref<LabelOperation>('artifact')
const payloadText = ref(prettyJson(samples.artifact))
const path = reactive({ productId: '', artifactId: '', reviewId: '' })
const statusQuery = reactive({
  productId: '', productVersion: 1, ageGradeDecisionVersion: 1,
  market: 'CN', language: 'zh-CN', artifactType: 'LABEL' as ArtifactType
})
const authStatus = computed(() => authSnapshot.value.status)
const authenticated = computed(() => authSnapshot.value.status === 'authenticated')
const accessToken = computed(() => authSnapshot.value.user?.access_token ?? '')
const profile = computed(() => authSnapshot.value.user?.profile as Readonly<Record<string, unknown>> | undefined)
const canManage = computed(() => authenticated.value && hasLabCapability(profile.value, 'toy.label.manage'))
const canReview = computed(() => authenticated.value && hasLabCapability(profile.value, 'toy.label.review'))
const canOperate = computed(() => operation.value === 'decision' ? canReview.value : canManage.value)
const state = useLabOperationState(authenticated, accessToken)

watch(operation, value => { payloadText.value = prettyJson(samples[value]) })

const blockedResponse = computed(() => {
  if (!isJsonRecord(state.response.value)) return false
  if (typeof state.response.value.decision === 'string') {
    return state.response.value.decision !== 'VALID'
  }
  const versions = state.response.value.versions
  if (!Array.isArray(versions) || versions.length === 0) return false
  const latest = versions.at(-1)
  return isJsonRecord(latest) && ['REJECTED', 'INVALIDATED'].includes(String(latest.state ?? ''))
})

async function submitOperation(): Promise<void> {
  const payload = readPayload()
  if (!payload || !validatePayload(payload) || !canOperate.value || !path.productId.trim()) {
    if (!path.productId.trim()) state.validate('标签写操作需要 product ID。')
    return
  }
  if ((operation.value === 'version' || operation.value === 'review') && !path.artifactId.trim()) {
    state.validate('当前操作需要 artifact ID。')
    return
  }
  if (operation.value === 'decision' && !path.reviewId.trim()) {
    state.validate('审核决定需要 review ID。')
    return
  }

  const productId = path.productId.trim()
  const result = await state.execute('Toy 标签审核操作已完成', async () => {
    if (operation.value === 'artifact') {
      return createToyLabelArtifact(
        productId, payload as unknown as CreateToyLabelArtifactRequest, context()
      )
    }
    if (operation.value === 'version') {
      return appendToyLabelArtifactVersion(
        productId, path.artifactId.trim(),
        payload as unknown as AppendToyLabelArtifactVersionRequest, context()
      )
    }
    if (operation.value === 'review') {
      return createToyLabelReview(
        productId, path.artifactId.trim(),
        payload as unknown as CreateToyLabelReviewRequest, context()
      )
    }
    return decideToyLabelReview(
      productId, path.reviewId.trim(), payload as unknown as DecideToyLabelReviewRequest, context()
    )
  }, submitOperation)
  if (result) {
    path.productId = result.productId
    statusQuery.productId = result.productId
    if ('artifactId' in result) path.artifactId = result.artifactId
    if ('reviewId' in result) path.reviewId = result.reviewId
  }
}

async function loadStatus(): Promise<void> {
  const query = statusQuery as ToyLabelReviewStatusQuery & { productId: string }
  if (!canManage.value || !query.productId.trim() || !positiveInteger(query.productVersion) ||
      !positiveInteger(query.ageGradeDecisionVersion) || !query.market.trim() || !query.language.trim()) {
    state.validate('状态查询需要管理能力、product ID、产品/年龄决定精确版本、市场和语言。')
    return
  }
  const result = await state.execute(
    'Toy 标签审核状态',
    () => getToyLabelReviewStatus(query.productId.trim(), {
      productVersion: query.productVersion,
      ageGradeDecisionVersion: query.ageGradeDecisionVersion,
      market: query.market.trim(), language: query.language.trim(), artifactType: query.artifactType
    }, context()),
    loadStatus
  )
  if (result) {
    path.productId = result.productId
    if (result.artifactId) path.artifactId = result.artifactId
    if (result.reviewId) path.reviewId = result.reviewId
  }
}

function readPayload(): JsonRecord | undefined {
  try { return parseJsonObject(payloadText.value) } catch (error) {
    state.validate(error instanceof Error ? error.message : '请求 JSON 无效。')
    return undefined
  }
}

function validatePayload(payload: JsonRecord): boolean {
  if (operation.value === 'decision') {
    const valid = hasPositiveInteger(payload, 'expectedCurrentVersion') &&
      typeof payload.decision === 'string' && ['APPROVED', 'REJECTED'].includes(payload.decision) &&
      hasRequiredString(payload, 'decisionReason')
    return state.validate(valid ? '' : '审核决定需要正整数版本、批准枚举和非空原因。')
  }
  if (operation.value === 'review') return validateReview(payload)
  const expectedValid = operation.value === 'artifact'
    ? payload.expectedCurrentVersion === 0
    : hasPositiveInteger(payload, 'expectedCurrentVersion')
  const valid = expectedValid && hasSha256(payload, 'contentHash') && validateImages(payload.imageEvidenceRefs)
  if (!valid) return state.validate('标签工件需要精确并发版本、SHA-256 内容哈希和不重复的图像对象证据。')
  if (operation.value === 'version') return state.validate('')
  const scope = payload.objectScope
  const artifactValid = isJsonRecord(scope) && hasRequiredString(scope, 'legalEntityId') &&
    hasRequiredString(scope, 'laboratoryId') && typeof payload.artifactType === 'string' &&
    ['PACKAGING', 'LABEL', 'INSTRUCTION', 'MARKETING_AGE_CLAIM'].includes(payload.artifactType) &&
    hasRequiredString(payload, 'language') && hasRequiredString(payload, 'market')
  return state.validate(artifactValid ? '' : '初始工件需要对象范围、批准类型、语言和市场。')
}

function validateReview(payload: JsonRecord): boolean {
  const baseValid = payload.ruleSetVersion === TOY_LABEL_REVIEW_RULE_SET_VERSION &&
    hasNonNegativeInteger(payload, 'expectedCurrentVersion') &&
    ['artifactVersion', 'productVersion', 'ageGradeDecisionVersion'].every(key => hasPositiveInteger(payload, key)) &&
    hasRequiredString(payload, 'market') && hasRequiredString(payload, 'language') &&
    hasArray(payload, 'reviewScopeRefs') && (payload.reviewScopeRefs as unknown[]).every(hasVersionedReference) &&
    uniqueReferences(payload.reviewScopeRefs as unknown[]) && hasVersionedReference(payload.impactRuleRef)
  if (!baseValid) return state.validate('审核需要固定规则集、精确产品/年龄/工件版本和不重复的范围/影响规则引用。')
  const currentVersion = payload.expectedCurrentVersion as number
  if (currentVersion === 0) {
    const initial = payload.previousReviewVersion === undefined && payload.triggerChange === undefined
    return state.validate(initial ? '' : '首次审核不能携带 previousReviewVersion 或 triggerChange。')
  }
  const trigger = payload.triggerChange
  const followUp = payload.previousReviewVersion === currentVersion && isJsonRecord(trigger) &&
    typeof trigger.changeType === 'string' && ['PRODUCT_VERSION', 'AGE_GRADE_DECISION'].includes(trigger.changeType) &&
    hasVersionedReference(trigger.changeRef)
  return state.validate(followUp ? '' : '后继审核必须绑定当前审核版本和已记录的产品或年龄决定变更。')
}

function validateImages(value: unknown): boolean {
  if (!Array.isArray(value) || value.length === 0) return false
  const keys = new Set<string>()
  return value.every(item => {
    if (!isJsonRecord(item) || !hasSha256(item, 'hash') || !isJsonRecord(item.objectRef) ||
        !hasRequiredString(item.objectRef, 'bucket') || !hasRequiredString(item.objectRef, 'objectKey')) return false
    const key = `${item.objectRef.bucket}\u0000${item.objectRef.objectKey}`
    if (keys.has(key)) return false
    keys.add(key)
    return true
  })
}

function uniqueReferences(values: unknown[]): boolean {
  const keys = values.map(value => isJsonRecord(value) ? `${value.id}\u0000${value.version}` : '')
  return keys.length === new Set(keys).size
}
function positiveInteger(value: number): boolean { return Number.isInteger(value) && value > 0 }
function context() { return { accessToken: accessToken.value } }
</script>

<template>
  <main class="lab-workbench-page">
    <header class="lab-workbench-heading">
      <p class="eyebrow">TOY WORKBENCH · LABEL REVIEW · {{ TOY_LABEL_REVIEW_RULE_SET_VERSION }}</p>
      <h1>玩具标签工件与审核</h1>
      <p>固定产品、年龄决定、工件、市场和语言版本，管理标签证据并记录受控审核决定。</p>
    </header>
    <LabAccessNotice :status="authStatus" :can-write="canManage || canReview" capability="toy.label.manage / toy.label.review" />

    <template v-if="authenticated">
      <form class="lab-panel" @submit.prevent="submitOperation">
        <h2>执行标签操作</h2>
        <div class="lab-grid">
          <label>操作<select v-model="operation" :disabled="state.busy.value"><option value="artifact">创建标签工件</option><option value="version">追加工件版本</option><option value="review">创建审核版本</option><option value="decision">记录审核决定</option></select></label>
          <label>Product ID<input v-model="path.productId" required :disabled="!canOperate || state.busy.value"></label>
          <label v-if="operation === 'version' || operation === 'review'">Artifact ID<input v-model="path.artifactId" required :disabled="!canOperate || state.busy.value"></label>
          <label v-if="operation === 'decision'">Review ID<input v-model="path.reviewId" required :disabled="!canOperate || state.busy.value"></label>
        </div>
        <p v-if="!canOperate" class="lab-validation" role="status">当前身份缺少 {{ operation === 'decision' ? 'toy.label.review' : 'toy.label.manage' }}。</p>
        <p class="lab-operation-note">页面只提交对象存储引用与 SHA-256，不上传图像正文；UNKNOWN、REJECTED、INVALIDATED 均失败关闭。</p>
        <LabJsonEditor v-model="payloadText" label="请求 JSON" :disabled="!canOperate || state.busy.value" />
        <div class="lab-actions"><button type="submit" :disabled="!canOperate || state.busy.value">提交标签操作</button></div>
      </form>

      <form class="lab-panel" @submit.prevent="loadStatus">
        <h2>查询精确标签审核状态</h2>
        <div class="lab-grid">
          <label>Product ID<input v-model="statusQuery.productId" required :disabled="!canManage || state.busy.value"></label>
          <label>Product version<input v-model.number="statusQuery.productVersion" type="number" min="1" step="1" required :disabled="!canManage || state.busy.value"></label>
          <label>Age decision version<input v-model.number="statusQuery.ageGradeDecisionVersion" type="number" min="1" step="1" required :disabled="!canManage || state.busy.value"></label>
          <label>市场<input v-model="statusQuery.market" required :disabled="!canManage || state.busy.value"></label>
          <label>语言<input v-model="statusQuery.language" required :disabled="!canManage || state.busy.value"></label>
          <label>工件类型<select v-model="statusQuery.artifactType" :disabled="!canManage || state.busy.value"><option value="PACKAGING">PACKAGING</option><option value="LABEL">LABEL</option><option value="INSTRUCTION">INSTRUCTION</option><option value="MARKETING_AGE_CLAIM">MARKETING_AGE_CLAIM</option></select></label>
        </div>
        <p v-if="state.validationError.value" class="lab-validation" role="alert">{{ state.validationError.value }}</p>
        <div class="lab-actions"><button type="submit" :disabled="!canManage || state.busy.value">查询状态</button></div>
      </form>
      <LabProblemAlert v-if="state.error.value" :error="state.error.value" @retry="state.retryLast" />
      <LabJsonResult v-if="state.response.value" :title="state.responseTitle.value" :value="state.response.value" :blocked="blockedResponse" />
    </template>
  </main>
</template>

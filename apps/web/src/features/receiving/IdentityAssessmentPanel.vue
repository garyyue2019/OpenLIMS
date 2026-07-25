<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from 'vue'
import { authSnapshot } from '../../auth-store'
import { hasIdentityEvaluateCapability } from './identity-access'
import {
  createIdentityObservation,
  getIdentityAssessment,
  IdentityApiError,
  identityRuleSetVersion,
  submitIdentityDecision,
  type IdentityAssessmentResult,
  type IdentityDecisionOutcome
} from './identity-client'

const props = defineProps<{ receivedItemId: string }>()
const emit = defineEmits<{ itemVersionChanged: [version: number] }>()
const assessment = ref<IdentityAssessmentResult>()
const loading = ref(false)
const errorCode = ref<string>()
const observationForm = reactive({
  labels: '',
  model: '',
  batch: '',
  appearance: '',
  attachmentRef: '',
  attachmentHash: ''
})
const decisionForm = reactive({
  outcome: 'MATCHED' as IdentityDecisionOutcome,
  reasonCode: 'CONSISTENT',
  rationale: ''
})

const profile = computed(() => authSnapshot.value.user?.profile as Record<string, unknown> | undefined)
const canEvaluate = computed(() =>
  authSnapshot.value.status === 'authenticated' && hasIdentityEvaluateCapability(profile.value)
)
const latestObservation = computed(() => assessment.value?.observations.at(-1))
const declaration = computed(() => assessment.value?.declarationSnapshot)
const modelConflict = computed(() =>
  declaration.value && observationForm.model.trim()
    ? declaration.value.model.trim().toLocaleLowerCase() !== observationForm.model.trim().toLocaleLowerCase()
    : false
)
const batchConflict = computed(() =>
  declaration.value && observationForm.batch.trim()
    ? declaration.value.batch.trim().toLocaleLowerCase() !== observationForm.batch.trim().toLocaleLowerCase()
    : false
)

onMounted(load)
watch(() => decisionForm.outcome, outcome => {
  decisionForm.reasonCode = outcome === 'INDETERMINATE'
    ? 'IDENTITY_AMBIGUOUS'
    : outcome === 'MISMATCHED' ? 'IDENTITY_CONFLICT' : 'CONSISTENT'
})

async function load(): Promise<void> {
  const token = authSnapshot.value.user?.access_token
  if (!canEvaluate.value || !token || loading.value) return
  loading.value = true
  errorCode.value = undefined
  try {
    assessment.value = await getIdentityAssessment(props.receivedItemId, token)
    emit('itemVersionChanged', assessment.value.itemVersion)
    const latest = assessment.value.observations.at(-1)
    if (latest) {
      observationForm.labels = latest.observedLabels.join(', ')
      observationForm.model = latest.observedModel
      observationForm.batch = latest.observedBatch
      observationForm.appearance = latest.appearance
      observationForm.attachmentRef = latest.attachmentRefs.at(0) ?? ''
      observationForm.attachmentHash = latest.attachmentHashes.at(0) ?? ''
    }
  } catch (error) {
    errorCode.value = error instanceof IdentityApiError ? error.errorCode : 'IDENTITY_NETWORK_ERROR'
  } finally {
    loading.value = false
  }
}

async function addObservation(): Promise<void> {
  const token = authSnapshot.value.user?.access_token
  if (!assessment.value || !canEvaluate.value || !token || loading.value) return
  loading.value = true
  errorCode.value = undefined
  try {
    assessment.value = await createIdentityObservation(props.receivedItemId, {
      expectedItemVersion: assessment.value.itemVersion,
      observedLabels: observationForm.labels.split(',').map(value => value.trim()).filter(Boolean),
      observedModel: observationForm.model.trim(),
      observedBatch: observationForm.batch.trim(),
      appearance: observationForm.appearance.trim(),
      attachmentRefs: [observationForm.attachmentRef.trim()].filter(Boolean),
      attachmentHashes: [observationForm.attachmentHash.trim()].filter(Boolean)
    }, token)
    emit('itemVersionChanged', assessment.value.itemVersion)
  } catch (error) {
    errorCode.value = error instanceof IdentityApiError ? error.errorCode : 'IDENTITY_NETWORK_ERROR'
  } finally {
    loading.value = false
  }
}

async function addDecision(): Promise<void> {
  const token = authSnapshot.value.user?.access_token
  const current = assessment.value
  const observation = latestObservation.value
  const snapshot = declaration.value
  if (!current || !observation || !snapshot || !canEvaluate.value || !token || loading.value) return
  loading.value = true
  errorCode.value = undefined
  try {
    assessment.value = await submitIdentityDecision(props.receivedItemId, {
      expectedItemVersion: current.itemVersion,
      observationVersion: observation.version,
      declarationSnapshotVersion: snapshot.snapshotVersion,
      outcome: decisionForm.outcome,
      reasonCode: decisionForm.reasonCode.trim(),
      rationale: decisionForm.rationale.trim(),
      ruleSetVersion: identityRuleSetVersion
    }, token)
    emit('itemVersionChanged', assessment.value.itemVersion)
    decisionForm.rationale = ''
  } catch (error) {
    errorCode.value = error instanceof IdentityApiError ? error.errorCode : 'IDENTITY_NETWORK_ERROR'
  } finally {
    loading.value = false
  }
}
</script>

<template>
  <section class="identity-panel" :aria-labelledby="`identity-${receivedItemId}`">
    <header>
      <h4 :id="`identity-${receivedItemId}`">身份评估工作台</h4>
      <p v-if="assessment">评估：<strong>{{ assessment.assessmentState }}</strong> · 对象版本 {{ assessment.itemVersion }}</p>
    </header>

    <a-alert
      type="warning"
      show-icon
      message="仍在隔离：身份结论不会解除 QUARANTINED"
      description="拆解、制样和检测分配继续统一阻断，必须等待后续受控放行决定。"
    />
    <a-alert
      v-if="!canEvaluate"
      type="warning"
      show-icon
      message="当前身份没有身份评估权限"
      description="系统管理员权限不会自动扩展为 receiving.identity.evaluate。"
    />

    <a-button v-if="canEvaluate && !assessment" :loading="loading" @click="load">载入评估</a-button>

    <div v-if="assessment" class="identity-columns">
      <section aria-labelledby="declaration-heading">
        <h5 id="declaration-heading">1. 客户声明快照</h5>
        <template v-if="declaration">
          <dl>
            <dt>描述</dt><dd>{{ declaration.declaredDescription }}</dd>
            <dt>型号</dt><dd>{{ declaration.model }}</dd>
            <dt>批次</dt><dd>{{ declaration.batch }}</dd>
            <dt>序列号</dt><dd>{{ declaration.serialNumber || '—' }}</dd>
            <dt>颜色</dt><dd>{{ declaration.color }}</dd>
          </dl>
          <small>快照 v{{ declaration.snapshotVersion }} · 对象 v{{ declaration.itemVersion }}</small>
        </template>
        <p v-else>首次提交完整实验室观察时固定声明快照。</p>
      </section>

      <form aria-labelledby="observation-heading" @submit.prevent="addObservation">
        <h5 id="observation-heading">2. 实验室观察</h5>
        <label>观察到的标签<input v-model="observationForm.labels" required :disabled="!canEvaluate || loading"></label>
        <label :class="{ conflict: modelConflict }">观察型号<input v-model="observationForm.model" required :disabled="!canEvaluate || loading"></label>
        <p v-if="modelConflict" class="difference">与声明型号不一致</p>
        <label :class="{ conflict: batchConflict }">观察批次<input v-model="observationForm.batch" required :disabled="!canEvaluate || loading"></label>
        <p v-if="batchConflict" class="difference">与声明批次不一致</p>
        <label>外观<input v-model="observationForm.appearance" required :disabled="!canEvaluate || loading"></label>
        <label>附件引用<input v-model="observationForm.attachmentRef" required :disabled="!canEvaluate || loading"></label>
        <label>附件 SHA-256<input v-model="observationForm.attachmentHash" minlength="64" maxlength="64" required :disabled="!canEvaluate || loading"></label>
        <a-button type="primary" html-type="submit" :loading="loading" :disabled="!canEvaluate">追加观察</a-button>
      </form>

      <form aria-labelledby="decision-heading" @submit.prevent="addDecision">
        <h5 id="decision-heading">3. 人工结论</h5>
        <label>结论
          <select v-model="decisionForm.outcome" :disabled="!canEvaluate || loading">
            <option value="MATCHED">MATCHED</option>
            <option value="MISMATCHED">MISMATCHED</option>
            <option value="INDETERMINATE">INDETERMINATE</option>
          </select>
        </label>
        <label>原因码<input v-model="decisionForm.reasonCode" required :disabled="!canEvaluate || loading"></label>
        <label>人工理由<textarea v-model="decisionForm.rationale" required :disabled="!canEvaluate || loading" /></label>
        <p>规则集：{{ identityRuleSetVersion }}</p>
        <a-button type="primary" html-type="submit" :loading="loading" :disabled="!canEvaluate || !latestObservation || !declaration">追加结论</a-button>
      </form>
    </div>

    <section v-if="assessment && (assessment.observations.length || assessment.decisions.length)" class="identity-history" aria-labelledby="identity-history-heading">
      <h5 id="identity-history-heading">只读版本历史</h5>
      <ol>
        <li v-for="observation in assessment.observations" :key="observation.observationId">
          观察 v{{ observation.version }} · {{ observation.observedModel }} / {{ observation.observedBatch }} · {{ observation.observedBy }}
        </li>
        <li v-for="decision in assessment.decisions" :key="decision.decisionId">
          结论 v{{ decision.version }} · <strong>{{ decision.outcome }}</strong> · {{ decision.reasonCode }} · {{ decision.decidedBy }}
        </li>
      </ol>
    </section>
    <a-alert v-if="errorCode" type="error" show-icon :message="`身份评估失败：${errorCode}`" description="刷新对象版本后再重试；失败请求不会覆盖历史事实。" />
  </section>
</template>

<style scoped>
.identity-panel { margin: 1rem 0; padding: 1rem; border: 1px solid #d9d9d9; border-radius: .5rem; }
.identity-columns { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 1rem; margin-top: 1rem; }
.identity-columns > section, .identity-columns > form { padding: 1rem; background: #fafafa; border-radius: .4rem; }
.identity-columns label { display: grid; gap: .25rem; margin-bottom: .65rem; }
.identity-columns input, .identity-columns select, .identity-columns textarea { width: 100%; }
.conflict input { border-color: #cf1322; background: #fff1f0; }
.difference { color: #a8071a; font-weight: 600; }
.identity-history { margin-top: 1rem; }
dl { display: grid; grid-template-columns: auto 1fr; gap: .35rem .75rem; }
dt { font-weight: 600; }
dd { margin: 0; }
@media (max-width: 960px) { .identity-columns { grid-template-columns: 1fr; } }
</style>

<script setup lang="ts">
import { reactive, ref } from 'vue'
import LabAccessNotice from './LabAccessNotice.vue'
import LabProblemAlert from './LabProblemAlert.vue'
import type { LabApiError } from './lab-api'
import { normalizeLabError, positiveInteger, useLabAccess } from './lab-view-state'
import {
  createQuantityAccount,
  getQuantityAccount,
  getQuantityAvailability,
  postQuantityEntry,
  QUANTITY_RULE_SET_VERSION,
  type CreateQuantityAccountRequest,
  type PostQuantityEntryRequest,
  type QuantityAccountResult,
  type QuantityAvailabilityResult,
  type QuantityEntryResult
} from './quantity-client'

const { authStatus, authenticated, accessToken, canWrite } = useLabAccess('quantity.post')
const accountForm = reactive<CreateQuantityAccountRequest>({
  ruleSetVersion: QUANTITY_RULE_SET_VERSION,
  objectScope: { legalEntityId: '', laboratoryId: '', customerId: '', serviceOrderId: '', productCategory: '' },
  subject: { subjectType: 'RECEIVED_ITEM', id: '', version: 1 },
  subjectQuantifiable: true,
  dimension: 'MASS',
  unit: 'GRAM',
  precisionScale: 2,
  conservationTolerance: 0
})
const entryForm = reactive({
  accountId: '', expectedCurrentVersion: 1,
  entryType: 'RECEIPT' as PostQuantityEntryRequest['entryType'], amount: 0,
  reason: '', referencedEntryId: '', reservationId: ''
})
const lookup = reactive({ accountId: '', expectedVersion: 1, requestedAmount: 1 })
const busy = ref(false)
const validationError = ref('')
const error = ref<LabApiError>()
const account = ref<QuantityAccountResult>()
const entry = ref<QuantityEntryResult>()
const availability = ref<QuantityAvailabilityResult>()
let retryAction: (() => Promise<void>) | undefined

async function createAccount(): Promise<void> {
  validationError.value = ''
  const scopeComplete = Object.values(accountForm.objectScope).every(value => value.trim())
  if (!scopeComplete || !accountForm.subject.id.trim() || !positiveInteger(accountForm.subject.version) ||
      !accountForm.unit.trim() || !Number.isInteger(accountForm.precisionScale) ||
      accountForm.precisionScale < 0 || accountForm.conservationTolerance < 0) {
    validationError.value = '请填写完整对象范围和主体；版本必须为正整数，精度和守恒容差不能为负。'
    return
  }
  await execute(async () => {
    account.value = await createQuantityAccount({
      ...accountForm,
      objectScope: { ...accountForm.objectScope },
      subject: { ...accountForm.subject, id: accountForm.subject.id.trim() },
      unit: accountForm.unit.trim()
    }, { accessToken: accessToken.value })
    entryForm.accountId = account.value.quantityAccountId
    entryForm.expectedCurrentVersion = account.value.version
    lookup.accountId = account.value.quantityAccountId
    lookup.expectedVersion = account.value.version
    entry.value = undefined
    availability.value = undefined
  }, createAccount)
}

async function postEntry(): Promise<void> {
  validationError.value = ''
  if (!entryForm.accountId.trim() || !positiveInteger(entryForm.expectedCurrentVersion) || entryForm.amount <= 0) {
    validationError.value = '追加流水需要账户 ID、正整数当前版本和大于 0 的数量。'
    return
  }
  await execute(async () => {
    entry.value = await postQuantityEntry(entryForm.accountId.trim(), {
      expectedCurrentVersion: entryForm.expectedCurrentVersion,
      ruleSetVersion: QUANTITY_RULE_SET_VERSION,
      entryType: entryForm.entryType,
      amount: entryForm.amount,
      reason: entryForm.reason.trim() || undefined,
      referencedEntryId: entryForm.referencedEntryId.trim() || undefined,
      reservationId: entryForm.reservationId.trim() || undefined
    }, { accessToken: accessToken.value })
    entryForm.expectedCurrentVersion = entry.value.accountVersion
    lookup.accountId = entry.value.quantityAccountId
    lookup.expectedVersion = entry.value.accountVersion
  }, postEntry)
}

async function loadAccount(): Promise<void> {
  validationError.value = ''
  if (!lookup.accountId.trim()) {
    validationError.value = '请输入数量账户 ID。'
    return
  }
  await execute(async () => {
    account.value = await getQuantityAccount(lookup.accountId.trim(), { accessToken: accessToken.value })
    lookup.expectedVersion = account.value.version
    entryForm.accountId = account.value.quantityAccountId
    entryForm.expectedCurrentVersion = account.value.version
  }, loadAccount)
}

async function checkAvailability(): Promise<void> {
  validationError.value = ''
  if (!lookup.accountId.trim() || !positiveInteger(lookup.expectedVersion) || lookup.requestedAmount <= 0) {
    validationError.value = '可用量检查需要账户 ID、正整数精确版本和大于 0 的请求量。'
    return
  }
  await execute(async () => {
    availability.value = await getQuantityAvailability(
      lookup.accountId.trim(), lookup.expectedVersion, lookup.requestedAmount,
      { accessToken: accessToken.value }
    )
  }, checkAvailability)
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
      <p class="eyebrow">LAB WORKBENCH · QUANTITY · {{ QUANTITY_RULE_SET_VERSION }}</p>
      <h1>数量账本</h1>
      <p>创建单一维度账户、追加不可变流水，并按精确版本检查余额和可用量。</p>
    </header>
    <LabAccessNotice :status="authStatus" :can-write="canWrite" capability="quantity.post" />

    <template v-if="authenticated">
      <form class="lab-panel" @submit.prevent="createAccount">
        <h2>创建数量账户</h2>
        <div class="lab-grid">
          <label>法人<input v-model="accountForm.objectScope.legalEntityId" required :disabled="!canWrite || busy"></label>
          <label>实验室<input v-model="accountForm.objectScope.laboratoryId" required :disabled="!canWrite || busy"></label>
          <label>客户<input v-model="accountForm.objectScope.customerId" required :disabled="!canWrite || busy"></label>
          <label>服务委托<input v-model="accountForm.objectScope.serviceOrderId" required :disabled="!canWrite || busy"></label>
          <label>产品类别<input v-model="accountForm.objectScope.productCategory" required :disabled="!canWrite || busy"></label>
          <label>主体类型<select v-model="accountForm.subject.subjectType" :disabled="!canWrite || busy"><option>RECEIVED_ITEM</option><option>DERIVED_SAMPLE</option><option>TEST_SPECIMEN</option></select></label>
          <label>主体 ID<input v-model="accountForm.subject.id" required :disabled="!canWrite || busy"></label>
          <label>主体版本<input v-model.number="accountForm.subject.version" type="number" min="1" step="1" required :disabled="!canWrite || busy"></label>
          <label>维度<select v-model="accountForm.dimension" :disabled="!canWrite || busy"><option>COUNT</option><option>MASS</option><option>LENGTH</option><option>AREA</option><option>VOLUME</option></select></label>
          <label>单位<input v-model="accountForm.unit" required :disabled="!canWrite || busy"></label>
          <label>小数精度<input v-model.number="accountForm.precisionScale" type="number" min="0" step="1" required :disabled="!canWrite || busy"></label>
          <label>守恒容差<input v-model.number="accountForm.conservationTolerance" type="number" min="0" step="any" required :disabled="!canWrite || busy"></label>
          <label><span>主体可计量</span><input v-model="accountForm.subjectQuantifiable" type="checkbox" :disabled="!canWrite || busy"></label>
        </div>
        <div class="lab-actions"><button type="submit" :disabled="!canWrite || busy">创建账户</button></div>
      </form>

      <form class="lab-panel" @submit.prevent="postEntry">
        <h2>追加数量流水</h2>
        <div class="lab-grid">
          <label>账户 ID<input v-model="entryForm.accountId" required :disabled="!canWrite || busy"></label>
          <label>当前精确版本<input v-model.number="entryForm.expectedCurrentVersion" type="number" min="1" step="1" required :disabled="!canWrite || busy"></label>
          <label>流水类型<select v-model="entryForm.entryType" :disabled="!canWrite || busy"><option>RECEIPT</option><option>OUTPUT</option><option>RESERVE</option><option>RESERVE_RELEASE</option><option>ALLOCATE</option><option>CONSUME</option><option>RETURN</option><option>LOSS</option><option>DISPOSE</option><option>REVERSAL</option><option>RESTATE</option></select></label>
          <label>数量<input v-model.number="entryForm.amount" type="number" min="0" step="any" required :disabled="!canWrite || busy"></label>
          <label>引用流水 ID<input v-model="entryForm.referencedEntryId" :disabled="!canWrite || busy"></label>
          <label>预留 ID<input v-model="entryForm.reservationId" :disabled="!canWrite || busy"></label>
          <label class="wide">原因<textarea v-model="entryForm.reason" :disabled="!canWrite || busy" /></label>
        </div>
        <div class="lab-actions"><button type="submit" :disabled="!canWrite || busy">追加流水</button></div>
      </form>

      <form class="lab-panel" @submit.prevent="loadAccount">
        <h2>账户详情与可用量</h2>
        <div class="lab-grid">
          <label>账户 ID<input v-model="lookup.accountId" required :disabled="busy"></label>
          <label>精确版本<input v-model.number="lookup.expectedVersion" type="number" min="1" step="1" required :disabled="busy"></label>
          <label>请求量<input v-model.number="lookup.requestedAmount" type="number" min="0" step="any" required :disabled="busy"></label>
        </div>
        <p v-if="validationError" class="lab-validation" role="alert">{{ validationError }}</p>
        <div class="lab-actions">
          <button type="submit" :disabled="busy">加载账户</button>
          <button type="button" class="secondary" :disabled="busy" @click="checkAvailability">检查可用量</button>
        </div>
      </form>

      <LabProblemAlert v-if="error" :error="error" @retry="retry" />

      <section v-if="account" class="lab-panel lab-result" aria-live="polite">
        <h2>数量账户</h2>
        <dl class="lab-details">
          <div><dt>账户 ID</dt><dd>{{ account.quantityAccountId }}</dd></div>
          <div><dt>精确版本</dt><dd>{{ account.version }}</dd></div>
          <div><dt>维度 / 单位</dt><dd>{{ account.dimension }} / {{ account.unit }}</dd></div>
          <div><dt>余额</dt><dd>{{ account.balance }}</dd></div>
          <div><dt>已预留</dt><dd>{{ account.reserved }}</dd></div>
          <div><dt>可用量</dt><dd>{{ account.available }}</dd></div>
        </dl>
      </section>

      <section v-if="entry" class="lab-panel lab-result" aria-live="polite">
        <h2>流水已记录</h2>
        <p>{{ entry.entryId }} · 账户版本 {{ entry.accountVersion }} · {{ entry.entryType }} {{ entry.amount }}</p>
        <p>结果：余额 {{ entry.resultingBalance }}，预留 {{ entry.resultingReserved }}，可用 {{ entry.resultingAvailable }}</p>
      </section>

      <section v-if="availability" class="lab-panel" :class="availability.decision === 'ALLOWED' ? 'lab-result' : 'lab-blocked'" aria-live="polite">
        <h2>可用量决定：{{ availability.decision }}</h2>
        <p>当前版本：{{ availability.currentAccountVersion ?? '不可用' }} · 可用量：{{ availability.availableAmount ?? '不可用' }}</p>
        <p>原因码：{{ availability.reasonCodes.length ? availability.reasonCodes.join('、') : '无' }}</p>
      </section>
    </template>
  </main>
</template>

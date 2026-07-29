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
  getInstrumentFile,
  getInstrumentImportStatus,
  INSTRUMENT_RULE_SET_VERSION,
  registerInstrumentFile,
  resolveInstrumentImportException,
  submitInstrumentRows,
  type RegisterInstrumentFileRequest,
  type ResolveImportExceptionRequest,
  type SubmitInstrumentRowsRequest
} from './instrument-client'

type InstrumentOperation = 'register' | 'rows' | 'resolve'

const samples: Record<InstrumentOperation, JsonRecord> = {
  register: {
    ruleSetVersion: INSTRUMENT_RULE_SET_VERSION,
    objectScope: { legalEntityId: 'legal-entity-id', laboratoryId: 'laboratory-id' },
    externalRef: { id: 'external-file-ref', version: 1 },
    sha256: 'a'.repeat(64),
    sourceSystem: 'INSTRUMENT',
    instrumentRef: { id: 'instrument-id', version: 1 },
    parserVersion: 'parser@1.0.0',
    declaredRowCount: 1
  },
  rows: {
    expectedCurrentVersion: 1,
    ruleSetVersion: INSTRUMENT_RULE_SET_VERSION,
    rows: [{
      rowNumber: 1,
      sampleNumber: 'sample-001',
      batchPosition: 'A01',
      parameter: 'parameter-code',
      unit: 'mg/kg',
      qualifier: '',
      rawValue: '1.20',
      parsedValue: '1.20'
    }]
  },
  resolve: {
    expectedCurrentVersion: 2,
    ruleSetVersion: INSTRUMENT_RULE_SET_VERSION,
    kind: 'REJECT_ROW',
    reason: 'Operator confirmed this row cannot be mapped.'
  }
}

const { authStatus, authenticated, accessToken, canWrite } = useLabAccess('instrument.import')
const operation = ref<InstrumentOperation>('register')
const payloadText = ref(prettyJson(samples.register))
const path = reactive({ fileId: '', exceptionId: '' })
const lookup = reactive({ fileId: '', expectedFileVersion: 1 })
const state = useLabOperationState(authenticated, accessToken)

watch(operation, value => { payloadText.value = prettyJson(samples[value]) })

const blockedResponse = computed(() => {
  if (!isJsonRecord(state.response.value)) return false
  return state.response.value.decision === 'BLOCKED' || state.response.value.decision === 'UNKNOWN' ||
    state.response.value.state === 'BLOCKED'
})

async function submitOperation(): Promise<void> {
  const payload = readPayload()
  if (!payload || !validatePayload(payload)) return
  if (operation.value !== 'register' && !path.fileId.trim()) {
    state.validate('追加行和异常处理需要仪器文件 ID。')
    return
  }
  if (operation.value === 'resolve' && !path.exceptionId.trim()) {
    state.validate('异常处理需要异常 ID。')
    return
  }

  const result = await state.execute('Instrument 写操作已完成', async () => {
    if (operation.value === 'register') {
      return registerInstrumentFile(payload as unknown as RegisterInstrumentFileRequest, context())
    }
    if (operation.value === 'rows') {
      return submitInstrumentRows(
        path.fileId.trim(), payload as unknown as SubmitInstrumentRowsRequest, context()
      )
    }
    return resolveInstrumentImportException(
      path.fileId.trim(), path.exceptionId.trim(),
      payload as unknown as ResolveImportExceptionRequest, context()
    )
  }, submitOperation)
  if (result) setCurrentFile(result.fileRegistrationId, result.version)
}

async function loadFile(): Promise<void> {
  if (!lookup.fileId.trim()) { state.validate('请输入仪器文件 ID。'); return }
  const result = await state.execute(
    'Instrument 文件详情',
    () => getInstrumentFile(lookup.fileId.trim(), context()),
    loadFile
  )
  if (result) setCurrentFile(result.fileRegistrationId, result.version)
}

async function checkStatus(): Promise<void> {
  if (!lookup.fileId.trim() || !positiveInteger(lookup.expectedFileVersion)) {
    state.validate('导入状态查询需要文件 ID 和正整数精确版本。')
    return
  }
  await state.execute(
    'Instrument 导入状态',
    () => getInstrumentImportStatus(lookup.fileId.trim(), lookup.expectedFileVersion, context()),
    checkStatus
  )
}

function readPayload(): JsonRecord | undefined {
  try { return parseJsonObject(payloadText.value) } catch (error) {
    state.validate(error instanceof Error ? error.message : '请求 JSON 无效。')
    return undefined
  }
}

function validatePayload(payload: JsonRecord): boolean {
  if (payload.ruleSetVersion !== INSTRUMENT_RULE_SET_VERSION) {
    return state.validate(`规则集必须固定为 ${INSTRUMENT_RULE_SET_VERSION}。`)
  }
  if (operation.value === 'register') {
    const scope = payload.objectScope
    const valid = isJsonRecord(scope) && hasRequiredString(scope, 'legalEntityId') &&
      hasRequiredString(scope, 'laboratoryId') && hasVersionedReference(payload.externalRef) &&
      hasVersionedReference(payload.instrumentRef) && hasSha256(payload, 'sha256') &&
      hasRequiredString(payload, 'sourceSystem') && hasRequiredString(payload, 'parserVersion') &&
      hasPositiveInteger(payload, 'declaredRowCount')
    return state.validate(valid ? '' : '登记需要完整对象范围、精确引用、64 位 SHA-256、来源、解析器版本和正整数行数。')
  }
  if (!hasPositiveInteger(payload, 'expectedCurrentVersion')) {
    return state.validate('追加或处理异常需要正整数 expectedCurrentVersion。')
  }
  if (operation.value === 'rows') {
    return state.validate(hasArray(payload, 'rows') ? '' : '解析行请求必须包含至少一行 rows。')
  }
  const kind = payload.kind
  const validKind = kind === 'ACCEPT_WITH_MAPPING' || kind === 'REJECT_ROW'
  const mappingValid = kind !== 'ACCEPT_WITH_MAPPING' || isJsonRecord(payload.correctedMapping)
  return state.validate(validKind && hasRequiredString(payload, 'reason') && mappingValid
    ? '' : '异常处理需要有效 kind、原因；接受映射时还需要 correctedMapping。')
}

function setCurrentFile(fileId: string, version: number): void {
  path.fileId = fileId
  lookup.fileId = fileId
  lookup.expectedFileVersion = version
}

function context() { return { accessToken: accessToken.value } }
</script>

<template>
  <main class="lab-workbench-page">
    <header class="lab-workbench-heading">
      <p class="eyebrow">LAB WORKBENCH · INSTRUMENT · {{ INSTRUMENT_RULE_SET_VERSION }}</p>
      <h1>仪器导入</h1>
      <p>登记外部文件证据，追加解析行，人工处理异常，并按精确版本检查导入状态。</p>
    </header>
    <LabAccessNotice :status="authStatus" :can-write="canWrite" capability="instrument.import" />

    <template v-if="authenticated">
      <form class="lab-panel" @submit.prevent="submitOperation">
        <h2>执行 Instrument 操作</h2>
        <div class="lab-grid">
          <label>操作
            <select v-model="operation" :disabled="!canWrite || state.busy.value">
              <option value="register">登记仪器文件</option>
              <option value="rows">追加解析行</option>
              <option value="resolve">处理导入异常</option>
            </select>
          </label>
          <label v-if="operation !== 'register'">仪器文件 ID
            <input v-model="path.fileId" required :disabled="!canWrite || state.busy.value">
          </label>
          <label v-if="operation === 'resolve'">异常 ID
            <input v-model="path.exceptionId" required :disabled="!canWrite || state.busy.value">
          </label>
        </div>
        <p class="lab-operation-note">示例覆盖该操作的完整批准 DTO；请使用真实稳定 ID 和精确版本替换示例值。</p>
        <LabJsonEditor v-model="payloadText" label="请求 JSON" :disabled="!canWrite || state.busy.value" />
        <div class="lab-actions"><button type="submit" :disabled="!canWrite || state.busy.value">提交写操作</button></div>
      </form>

      <form class="lab-panel" @submit.prevent="loadFile">
        <h2>文件详情与导入状态</h2>
        <div class="lab-grid">
          <label>仪器文件 ID<input v-model="lookup.fileId" required :disabled="state.busy.value"></label>
          <label>文件精确版本<input v-model.number="lookup.expectedFileVersion" type="number" min="1" step="1" required :disabled="state.busy.value"></label>
        </div>
        <p v-if="state.validationError.value" class="lab-validation" role="alert">{{ state.validationError.value }}</p>
        <div class="lab-actions">
          <button type="submit" :disabled="state.busy.value">加载文件详情</button>
          <button type="button" class="secondary" :disabled="state.busy.value" @click="checkStatus">检查导入状态</button>
        </div>
      </form>

      <LabProblemAlert v-if="state.error.value" :error="state.error.value" @retry="state.retryLast" />
      <LabJsonResult v-if="state.response.value" :title="state.responseTitle.value" :value="state.response.value" :blocked="blockedResponse" />
    </template>
  </main>
</template>

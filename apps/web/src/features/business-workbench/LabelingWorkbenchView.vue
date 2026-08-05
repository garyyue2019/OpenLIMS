<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'
import { authSnapshot } from '../../auth-store'
import LabAccessNotice from '../lab-workbench/LabAccessNotice.vue'
import LabJsonEditor from '../lab-workbench/LabJsonEditor.vue'
import LabJsonResult from '../lab-workbench/LabJsonResult.vue'
import LabProblemAlert from '../lab-workbench/LabProblemAlert.vue'
import {
  hasPositiveInteger,
  hasRequiredString,
  isJsonRecord,
  parseJsonObject,
  prettyJson,
  type JsonRecord
} from '../lab-workbench/lab-json'
import { useLabOperationState } from '../lab-workbench/lab-operation-state'
import {
  hasLabelPrintCapability,
  hasLabelReprintCapability,
  hasLabelScanCapability
} from '../receiving/labeling-access'
import {
  createLabelJobs,
  getLabelJob,
  reprintLabel,
  resolveLabelScan,
  type CreateLabelJobsRequest
} from '../receiving/labeling-client'
import { createIdempotencyKey } from '../receiving/receiving-client'

type LabelingOperation = 'create' | 'reprint' | 'scan'

const samples: Record<LabelingOperation, JsonRecord> = {
  create: {
    printerId: 'receiving-lab-a',
    targets: [{ objectType: 'RI', objectId: 'received-item-id', objectVersion: 1 }]
  },
  reprint: {
    printerId: 'receiving-lab-a',
    reason: 'Controlled reprint after damaged label.'
  },
  scan: {
    barcodePayload: 'OL1:RI:opaque-reference:checksum'
  }
}

const operation = ref<LabelingOperation>('create')
const payloadText = ref(prettyJson(samples.create))
const path = reactive({ printJobId: '' })
const lookup = reactive({ printJobId: '' })
const authStatus = computed(() => authSnapshot.value.status)
const authenticated = computed(() => authSnapshot.value.status === 'authenticated')
const accessToken = computed(() => authSnapshot.value.user?.access_token ?? '')
const profile = computed(() =>
  authSnapshot.value.user?.profile as Record<string, unknown> | undefined
)
const canPrint = computed(() => authenticated.value && hasLabelPrintCapability(profile.value))
const canScan = computed(() => authenticated.value && hasLabelScanCapability(profile.value))
const canReprint = computed(() => authenticated.value && hasLabelReprintCapability(profile.value))
const canAnyAction = computed(() => canPrint.value || canScan.value || canReprint.value)
const canOperate = computed(() => {
  if (operation.value === 'create') return canPrint.value
  if (operation.value === 'scan') return canScan.value
  return canReprint.value
})
const requiredCapability = computed(() => {
  if (operation.value === 'create') return 'receiving.label.print'
  if (operation.value === 'scan') return 'receiving.label.scan'
  return 'receiving.label.reprint'
})
const state = useLabOperationState(authenticated, accessToken)
let idempotencyKey: string | undefined

watch(operation, value => {
  payloadText.value = prettyJson(samples[value])
  idempotencyKey = undefined
})
watch([payloadText, () => path.printJobId], () => {
  if (!state.busy.value) idempotencyKey = undefined
})

const blockedResponse = computed(() => {
  if (!isJsonRecord(state.response.value)) return false
  return state.response.value.status === 'UNKNOWN' ||
    state.response.value.printVerificationStatus === 'UNKNOWN'
})

async function submitOperation(): Promise<void> {
  const payload = readPayload()
  if (!payload || !validatePayload(payload) || !canOperate.value) return
  const token = accessToken.value

  if (operation.value === 'scan') {
    await state.execute(
      'Labeling 扫码解析',
      () => resolveLabelScan(String(payload.barcodePayload).trim(), token),
      submitOperation
    )
    return
  }

  idempotencyKey ??= createIdempotencyKey()
  const key = idempotencyKey
  const result = await state.execute('Labeling 写操作已完成', async () => {
    if (operation.value === 'create') {
      return createLabelJobs(payload as unknown as CreateLabelJobsRequest, token, key)
    }
    return reprintLabel(
      path.printJobId.trim(),
      String(payload.printerId).trim(),
      String(payload.reason).trim(),
      token,
      key
    )
  }, submitOperation)

  if (result) {
    idempotencyKey = undefined
    const first = result.jobs[0]
    if (first) {
      path.printJobId = first.printJobId
      lookup.printJobId = first.printJobId
    }
  }
}

async function loadJob(): Promise<void> {
  if (!lookup.printJobId.trim()) {
    state.validate('请输入 print job ID。')
    return
  }
  const result = await state.execute(
    'Labeling 打印任务详情',
    () => getLabelJob(lookup.printJobId.trim(), accessToken.value),
    loadJob
  )
  if (result) path.printJobId = result.printJobId
}

function readPayload(): JsonRecord | undefined {
  try {
    return parseJsonObject(payloadText.value)
  } catch (error) {
    state.validate(error instanceof Error ? error.message : '请求 JSON 无效。')
    return undefined
  }
}

function validatePayload(payload: JsonRecord): boolean {
  if (operation.value === 'scan') {
    return state.validate(hasRequiredString(payload, 'barcodePayload') ? '' : '扫码解析需要非空 barcodePayload。')
  }
  if (operation.value === 'reprint') {
    const valid = path.printJobId.trim().length > 0 &&
      hasRequiredString(payload, 'printerId') && hasRequiredString(payload, 'reason')
    return state.validate(valid ? '' : '受控重印需要 print job ID、打印机 ID 和非空原因。')
  }
  const targets = payload.targets
  const validTargets = Array.isArray(targets) && targets.length > 0 && targets.every(target =>
    isJsonRecord(target) && (target.objectType === 'CT' || target.objectType === 'RI') &&
    hasRequiredString(target, 'objectId') && hasPositiveInteger(target, 'objectVersion')
  )
  return state.validate(
    hasRequiredString(payload, 'printerId') && validTargets ? '' :
      '创建任务需要打印机 ID，以及至少一个 CT/RI 稳定对象 ID 和正整数精确版本。'
  )
}
</script>

<template>
  <main class="lab-workbench-page">
    <header class="lab-workbench-heading">
      <p class="eyebrow">BUSINESS WORKBENCH · LABELING · LABELING@1.0.0</p>
      <h1>标签打印、重印与扫码</h1>
      <p>独立处理现有包装和实物标签；已发送不等于已出纸，扫码校验后才是 VERIFIED。</p>
    </header>

    <LabAccessNotice
      :status="authStatus"
      :can-write="canAnyAction"
      capability="receiving.label.print / scan / reprint"
    />

    <template v-if="authenticated">
      <section class="lab-panel" aria-labelledby="label-capabilities-heading">
        <h2 id="label-capabilities-heading">当前标签能力</h2>
        <dl class="lab-details">
          <div><dt>首次打印</dt><dd>{{ canPrint ? '允许' : '无 receiving.label.print' }}</dd></div>
          <div><dt>扫码解析</dt><dd>{{ canScan ? '允许' : '无 receiving.label.scan' }}</dd></div>
          <div><dt>受控重印</dt><dd>{{ canReprint ? '允许' : '无 receiving.label.reprint' }}</dd></div>
        </dl>
      </section>

      <form class="lab-panel" @submit.prevent="submitOperation">
        <h2>执行 Labeling 操作</h2>
        <div class="lab-grid">
          <label>操作
            <select v-model="operation" :disabled="state.busy.value">
              <option value="create">创建打印任务</option>
              <option value="reprint">受控重印一份</option>
              <option value="scan">扫码解析与校验</option>
            </select>
          </label>
          <label v-if="operation === 'reprint'">Print job ID
            <input v-model="path.printJobId" required :disabled="!canOperate || state.busy.value">
          </label>
        </div>
        <p v-if="!canOperate" class="lab-validation" role="status">当前身份没有 {{ requiredCapability }} 能力。</p>
        <p class="lab-operation-note">二维码不是授权凭证；服务端会重新校验对象范围。UNKNOWN 不会自动重发。</p>
        <LabJsonEditor v-model="payloadText" label="请求 JSON" :disabled="!canOperate || state.busy.value" />
        <div class="lab-actions">
          <button type="submit" :disabled="!canOperate || state.busy.value">提交操作</button>
        </div>
      </form>

      <form class="lab-panel" @submit.prevent="loadJob">
        <h2>查询打印任务</h2>
        <div class="lab-grid">
          <label>Print job ID
            <input v-model="lookup.printJobId" required :disabled="state.busy.value">
          </label>
        </div>
        <p v-if="state.validationError.value" class="lab-validation" role="alert">{{ state.validationError.value }}</p>
        <div class="lab-actions">
          <button type="submit" :disabled="state.busy.value">查询任务</button>
        </div>
      </form>

      <LabProblemAlert v-if="state.error.value" :error="state.error.value" @retry="state.retryLast" />
      <LabJsonResult
        v-if="state.response.value"
        :title="state.responseTitle.value"
        :value="state.response.value"
        :blocked="blockedResponse"
      />
    </template>
  </main>
</template>

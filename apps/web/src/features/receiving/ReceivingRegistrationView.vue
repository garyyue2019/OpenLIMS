<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'
import { authSnapshot } from '../../auth-store'
import { hasReceivingRegisterCapability } from './receiving-access'
import IdentityAssessmentPanel from './IdentityAssessmentPanel.vue'
import ReceivingExceptionPanel from './ReceivingExceptionPanel.vue'
import ReceivingReleasePanel from './ReceivingReleasePanel.vue'
import {
  hasLabelPrintCapability,
  hasLabelReprintCapability,
  hasLabelScanCapability
} from './labeling-access'
import {
  createLabelJobs,
  getLabelJob,
  LabelingApiError,
  reprintLabel,
  resolveLabelScan,
  type LabelPrintJobResult,
  type LabelPrintTarget,
  type LabelScanResolution
} from './labeling-client'
import {
  createIdempotencyKey,
  ReceiptApiError,
  registerReceipt,
  type RegisterContainerRequest,
  type RegisterReceiptRequest,
  type RegisterReceivedItemRequest,
  type ReceiptRegistrationResult
} from './receiving-client'

type EditableItem = Omit<RegisterReceivedItemRequest, 'quantity'> & { quantity: 1 }
type EditableContainer = Omit<RegisterContainerRequest, 'receivedItems'> & { receivedItems: EditableItem[] }

const form = reactive({
  legalEntityId: '',
  laboratoryId: '',
  customerId: '',
  serviceOrderId: '',
  arrivalAt: new Date().toISOString().slice(0, 16),
  containers: [newContainer()] as EditableContainer[]
})
const submitting = ref(false)
const result = ref<ReceiptRegistrationResult>()
const errorCode = ref<string>()
const labelErrorCode = ref<string>()
const labelJobs = ref<LabelPrintJobResult[]>([])
const printerId = ref('')
const scanPayload = ref('')
const scanResult = ref<LabelScanResolution>()
const labelBusy = ref(false)
const reprintReasons = reactive<Record<string, string>>({})
let idempotencyKey: string | undefined
let printIdempotencyKey: string | undefined
const reprintIdempotencyKeys = new Map<string, string>()

const userProfile = computed(() => authSnapshot.value.user?.profile as Record<string, unknown> | undefined)
const canRegister = computed(() =>
  authSnapshot.value.status === 'authenticated' && hasReceivingRegisterCapability(userProfile.value)
)
const canPrint = computed(() =>
  authSnapshot.value.status === 'authenticated' && hasLabelPrintCapability(userProfile.value)
)
const canScan = computed(() =>
  authSnapshot.value.status === 'authenticated' && hasLabelScanCapability(userProfile.value)
)
const canReprint = computed(() =>
  authSnapshot.value.status === 'authenticated' && hasLabelReprintCapability(userProfile.value)
)
const printTargets = computed<LabelPrintTarget[]>(() => {
  if (!result.value) return []
  return result.value.containers.flatMap(container => {
    const targets: LabelPrintTarget[] = []
    if (container.labelIdentity) {
      targets.push({ objectType: 'CT', objectId: container.containerId, objectVersion: 1 })
    }
    for (const item of container.receivedItems) {
      if (item.labelIdentity) {
        targets.push({ objectType: 'RI', objectId: item.receivedItemId, objectVersion: item.version })
      }
    }
    return targets
  })
})

watch(form, () => {
  if (!submitting.value) idempotencyKey = undefined
}, { deep: true })

function newItem(): EditableItem {
  return {
    declaredDescription: '',
    model: '',
    batch: '',
    serialNumber: '',
    color: '',
    packageCondition: 'intact',
    sealCondition: 'sealed',
    itemCondition: 'intact',
    quantity: 1,
    unit: 'set'
  }
}

function newContainer(): EditableContainer {
  return {
    externalLabel: '',
    packageType: 'carton',
    condition: 'intact',
    sealObservation: '',
    receivedItems: [newItem()]
  }
}

function addContainer(): void { form.containers.push(newContainer()) }
function addItem(container: EditableContainer): void { container.receivedItems.push(newItem()) }

async function submit(): Promise<void> {
  const token = authSnapshot.value.user?.access_token
  if (!canRegister.value || !token || submitting.value) return
  submitting.value = true
  errorCode.value = undefined
  result.value = undefined
  idempotencyKey ??= createIdempotencyKey()
  try {
    const request: RegisterReceiptRequest = {
      legalEntityId: form.legalEntityId.trim(),
      laboratoryId: form.laboratoryId.trim(),
      customerId: form.customerId.trim(),
      serviceOrderId: form.serviceOrderId.trim(),
      arrivalAt: new Date(form.arrivalAt).toISOString(),
      containers: form.containers.map(container => ({
        externalLabel: container.externalLabel?.trim() || undefined,
        packageType: container.packageType.trim(),
        condition: container.condition.trim(),
        sealObservation: container.sealObservation?.trim() || undefined,
        receivedItems: container.receivedItems.map(item => ({
          ...item,
          declaredDescription: item.declaredDescription.trim(),
          model: item.model.trim(),
          batch: item.batch.trim(),
          serialNumber: item.serialNumber?.trim() || undefined,
          color: item.color.trim(),
          unit: item.unit.trim(),
          quantity: 1
        }))
      }))
    }
    result.value = await registerReceipt(request, token, idempotencyKey)
    labelJobs.value = []
    printIdempotencyKey = undefined
    idempotencyKey = undefined
  } catch (error) {
    errorCode.value = error instanceof ReceiptApiError ? error.errorCode : 'REC.NETWORK_ERROR'
  } finally {
    submitting.value = false
  }
}

async function printAllLabels(): Promise<void> {
  const token = authSnapshot.value.user?.access_token
  if (!canPrint.value || !token || labelBusy.value || !printerId.value.trim() || printTargets.value.length === 0) return
  labelBusy.value = true
  labelErrorCode.value = undefined
  printIdempotencyKey ??= createIdempotencyKey()
  try {
    const response = await createLabelJobs({
      printerId: printerId.value.trim(),
      targets: printTargets.value
    }, token, printIdempotencyKey)
    labelJobs.value = response.jobs
    printIdempotencyKey = undefined
  } catch (error) {
    labelErrorCode.value = error instanceof LabelingApiError ? error.errorCode : 'LABEL.NETWORK_ERROR'
  } finally {
    labelBusy.value = false
  }
}

async function refreshJob(index: number): Promise<void> {
  const token = authSnapshot.value.user?.access_token
  const job = labelJobs.value[index]
  if (!token || !job || labelBusy.value) return
  labelBusy.value = true
  labelErrorCode.value = undefined
  try {
    labelJobs.value[index] = await getLabelJob(job.printJobId, token)
  } catch (error) {
    labelErrorCode.value = error instanceof LabelingApiError ? error.errorCode : 'LABEL.NETWORK_ERROR'
  } finally {
    labelBusy.value = false
  }
}

async function scanLabel(): Promise<void> {
  const token = authSnapshot.value.user?.access_token
  const payload = scanPayload.value.trim()
  if (!canScan.value || !token || !payload || labelBusy.value) return
  labelBusy.value = true
  labelErrorCode.value = undefined
  scanResult.value = undefined
  try {
    scanResult.value = await resolveLabelScan(payload, token)
    scanPayload.value = ''
  } catch (error) {
    labelErrorCode.value = error instanceof LabelingApiError ? error.errorCode : 'LABEL.NETWORK_ERROR'
  } finally {
    labelBusy.value = false
  }
}

async function reprint(job: LabelPrintJobResult): Promise<void> {
  const token = authSnapshot.value.user?.access_token
  const reason = reprintReasons[job.printJobId]?.trim()
  if (!canReprint.value || !token || !reason || labelBusy.value || !printerId.value.trim()) return
  labelBusy.value = true
  labelErrorCode.value = undefined
  const key = reprintIdempotencyKeys.get(job.printJobId) ?? createIdempotencyKey()
  reprintIdempotencyKeys.set(job.printJobId, key)
  try {
    const response = await reprintLabel(job.printJobId, printerId.value.trim(), reason, token, key)
    labelJobs.value.push(...response.jobs)
    reprintReasons[job.printJobId] = ''
    reprintIdempotencyKeys.delete(job.printJobId)
  } catch (error) {
    labelErrorCode.value = error instanceof LabelingApiError ? error.errorCode : 'LABEL.NETWORK_ERROR'
  } finally {
    labelBusy.value = false
  }
}
</script>

<template>
  <main class="receiving-page">
    <header>
      <p class="eyebrow">RECEIVING · DEV-006</p>
      <h1>到货、包装与实物登记</h1>
      <p>一个包装内有多个完整玩具或套装时，请逐个添加实物。登记成功后所有实物自动进入隔离。</p>
    </header>

    <a-alert
      v-if="!canRegister"
      type="warning"
      show-icon
      message="当前身份没有收样登记权限"
      description="页面处于只读状态。系统管理员默认不会自动获得 receiving.register 权限。"
    />

    <form class="receiving-form" @submit.prevent="submit">
      <section class="form-section" aria-labelledby="receipt-heading">
        <h2 id="receipt-heading">到货信息</h2>
        <div class="field-grid">
          <label>归属法人<input v-model="form.legalEntityId" required :disabled="!canRegister || submitting"></label>
          <label>收样实验室<input v-model="form.laboratoryId" required :disabled="!canRegister || submitting"></label>
          <label>客户<input v-model="form.customerId" required :disabled="!canRegister || submitting"></label>
          <label>服务委托<input v-model="form.serviceOrderId" required :disabled="!canRegister || submitting"></label>
          <label>到货时间<input v-model="form.arrivalAt" type="datetime-local" required :disabled="!canRegister || submitting"></label>
        </div>
      </section>

      <section
        v-for="(container, containerIndex) in form.containers"
        :key="containerIndex"
        class="form-section container-card"
        :aria-labelledby="`container-${containerIndex}`"
      >
        <h2 :id="`container-${containerIndex}`">包装 {{ containerIndex + 1 }}</h2>
        <div class="field-grid">
          <label>外部标签<input v-model="container.externalLabel" :disabled="!canRegister || submitting"></label>
          <label>包装类型<input v-model="container.packageType" required :disabled="!canRegister || submitting"></label>
          <label>包装状态<input v-model="container.condition" required :disabled="!canRegister || submitting"></label>
          <label>封识观察<input v-model="container.sealObservation" :disabled="!canRegister || submitting"></label>
        </div>

        <article v-for="(item, itemIndex) in container.receivedItems" :key="itemIndex" class="item-card">
          <h3>完整玩具或套装 {{ itemIndex + 1 }}</h3>
          <div class="field-grid">
            <label class="wide">声明描述<input v-model="item.declaredDescription" required :disabled="!canRegister || submitting"></label>
            <label>型号<input v-model="item.model" required :disabled="!canRegister || submitting"></label>
            <label>批次<input v-model="item.batch" required :disabled="!canRegister || submitting"></label>
            <label>序列号<input v-model="item.serialNumber" :disabled="!canRegister || submitting"></label>
            <label>颜色<input v-model="item.color" required :disabled="!canRegister || submitting"></label>
            <label>实物状态<input v-model="item.itemCondition" required :disabled="!canRegister || submitting"></label>
            <label>包装状态<input v-model="item.packageCondition" required :disabled="!canRegister || submitting"></label>
            <label>封识状态<input v-model="item.sealCondition" required :disabled="!canRegister || submitting"></label>
            <label>计量单位<input v-model="item.unit" required :disabled="!canRegister || submitting"></label>
          </div>
        </article>
        <a-button type="dashed" :disabled="!canRegister || submitting" @click="addItem(container)">添加另一个完整实物</a-button>
      </section>

      <div class="form-actions">
        <a-button type="dashed" :disabled="!canRegister || submitting" @click="addContainer">添加包装</a-button>
        <a-button type="primary" html-type="submit" :loading="submitting" :disabled="!canRegister">提交并进入隔离</a-button>
      </div>
    </form>

    <a-alert v-if="errorCode" type="error" show-icon :message="`登记失败：${errorCode}`" description="请修正资料后重试；未成功的请求不会留下半成品登记。" />

    <section v-if="result" class="registration-result" aria-live="polite">
      <h2>登记成功</h2>
      <p>到货号：<strong>{{ result.receiptNumber }}</strong></p>
      <div v-for="container in result.containers" :key="container.containerId">
        <h3>包装号：{{ container.containerNumber }}</h3>
        <p v-if="container.labelIdentity">
          包装标签：<strong>{{ container.labelIdentity.businessNumber }}</strong>
          · 模板 {{ container.labelIdentity.templateVersion }}
        </p>
        <ul>
          <li v-for="item in container.receivedItems" :key="item.receivedItemId">
            {{ item.receivedItemNumber }} · <strong>{{ item.state }}</strong>
            <span v-if="item.labelIdentity"> · 实物标签 {{ item.labelIdentity.businessNumber }}</span>
            <IdentityAssessmentPanel :received-item-id="item.receivedItemId" @item-version-changed="item.version = $event" />
            <ReceivingExceptionPanel :received-item-id="item.receivedItemId" :item-version="item.version" @item-version-changed="item.version = $event" />
            <ReceivingReleasePanel
              :received-item-id="item.receivedItemId"
              :item-version="item.version"
              :item-state="item.state"
              @item-version-changed="item.version = $event"
              @item-state-changed="item.state = $event"
            />
          </li>
        </ul>
      </div>

      <section class="form-section" aria-labelledby="label-print-heading">
        <h3 id="label-print-heading">批量打印 50×30mm 包装/实物标签</h3>
        <p>“已发送”只表示任务已发给打印机；扫码成功后才显示“已校验”。</p>
        <label>逻辑打印机 ID
          <input v-model="printerId" placeholder="例如 receiving-lab-a" :disabled="!canPrint || labelBusy">
        </label>
        <a-button type="primary" :loading="labelBusy" :disabled="!canPrint || !printerId.trim()" @click="printAllLabels">
          打印全部 {{ printTargets.length }} 张标签
        </a-button>
      </section>

      <section v-if="labelJobs.length" class="form-section" aria-labelledby="print-jobs-heading">
        <h3 id="print-jobs-heading">打印任务</h3>
        <article v-for="(job, index) in labelJobs" :key="job.printJobId" class="item-card">
          <strong>{{ job.objectType === 'CT' ? '包装' : '实物' }} · {{ job.businessNumber }}</strong>
          <p>状态：{{ job.status }} · 成功重印 {{ job.successfulReprintCount }} 次</p>
          <p v-if="job.status === 'UNKNOWN'">发送结果不确定：请先扫描疑似标签，或填写原因执行受控重印；禁止普通重试。</p>
          <a-button :disabled="labelBusy" @click="refreshJob(index)">刷新状态</a-button>
          <template v-if="canReprint">
            <label>重印原因<input v-model="reprintReasons[job.printJobId]" maxlength="500" :disabled="labelBusy"></label>
            <a-button :disabled="labelBusy || !reprintReasons[job.printJobId]?.trim()" @click="reprint(job)">受控重印一张</a-button>
          </template>
        </article>
      </section>
    </section>

    <section class="form-section" aria-labelledby="scan-heading">
      <h2 id="scan-heading">扫码校验</h2>
      <p>使用 USB/蓝牙扫码枪输入后按回车。服务端仍会重新校验法人、实验室、客户和对象权限。</p>
      <form @submit.prevent="scanLabel">
        <label>二维码内容<input v-model="scanPayload" autocomplete="off" :disabled="!canScan || labelBusy"></label>
        <a-button type="primary" html-type="submit" :disabled="!canScan || !scanPayload.trim()" :loading="labelBusy">解析并校验</a-button>
      </form>
      <div v-if="scanResult" aria-live="polite">
        <strong>{{ scanResult.objectType === 'CT' ? '包装' : '实物' }} · {{ scanResult.businessNumber }}</strong>
        <p>对象状态：{{ scanResult.state }} · 打印校验：{{ scanResult.printVerificationStatus }}</p>
      </div>
    </section>

    <a-alert v-if="labelErrorCode" type="error" show-icon :message="`标签操作失败：${labelErrorCode}`" description="失败、拒绝和不确定投递均会保留证据；请勿绕过门禁重复发送。" />
  </main>
</template>

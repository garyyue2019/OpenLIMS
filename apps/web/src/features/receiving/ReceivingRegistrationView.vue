<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'
import { authSnapshot } from '../../auth-store'
import { hasReceivingRegisterCapability } from './receiving-access'
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
let idempotencyKey: string | undefined

const userProfile = computed(() => authSnapshot.value.user?.profile as Record<string, unknown> | undefined)
const canRegister = computed(() =>
  authSnapshot.value.status === 'authenticated' && hasReceivingRegisterCapability(userProfile.value)
)

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
    idempotencyKey = undefined
  } catch (error) {
    errorCode.value = error instanceof ReceiptApiError ? error.errorCode : 'REC.NETWORK_ERROR'
  } finally {
    submitting.value = false
  }
}
</script>

<template>
  <main class="receiving-page">
    <header>
      <p class="eyebrow">RECEIVING · DEV-003</p>
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
        <ul>
          <li v-for="item in container.receivedItems" :key="item.receivedItemId">
            {{ item.receivedItemNumber }} · <strong>{{ item.state }}</strong>
          </li>
        </ul>
      </div>
    </section>
  </main>
</template>

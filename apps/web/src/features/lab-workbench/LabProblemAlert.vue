<script setup lang="ts">
import type { LabApiError } from './lab-api'

defineProps<{ error: LabApiError }>()
defineEmits<{ retry: [] }>()
</script>

<template>
  <section class="lab-problem" role="alert" aria-live="assertive">
    <h2>操作未完成</h2>
    <dl class="lab-details">
      <div><dt>错误码</dt><dd>{{ error.errorCode }}</dd></div>
      <div><dt>HTTP 状态</dt><dd>{{ error.status || '网络错误' }}</dd></div>
      <div><dt>关联 ID</dt><dd>{{ error.correlationId }}</dd></div>
      <div v-if="error.gateSource"><dt>阻断来源</dt><dd>{{ error.gateSource }}</dd></div>
    </dl>
    <p v-if="error.detail">{{ error.detail }}</p>
    <p v-if="error.nextAction"><strong>下一步：</strong>{{ error.nextAction }}</p>
    <button v-if="error.retryable" type="button" @click="$emit('retry')">显式重试</button>
  </section>
</template>

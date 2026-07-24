<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { authSnapshot, runtimeConfig, signIn } from '../auth-store'
import { getSystemStatus, type SystemStatus } from '../status-client'

const status = ref<SystemStatus>()
const configError = computed(() => authSnapshot.value.status === 'configuration-error')
async function refresh() { if (runtimeConfig.value) status.value = await getSystemStatus(runtimeConfig.value, authSnapshot.value.user?.access_token) }
onMounted(refresh)
</script>

<template>
  <main class="status-page" aria-labelledby="status-title">
    <a-card title="System status" :bordered="false">
      <h1 id="status-title" class="sr-only">System status</h1>
      <a-alert v-if="configError" type="error" show-icon message="Configuration error" description="System status cannot be checked until runtime configuration is valid." role="alert" />
      <a-spin v-else-if="!status" tip="Checking service status…" />
      <template v-else>
        <a-result v-if="status.health === 'ready'" status="success" title="Service ready" sub-title="Technical service responded normally." />
        <a-result v-else-if="status.health === 'unauthorized' || status.health === 'forbidden'" status="warning" :title="status.health === 'unauthorized' ? 'Session required' : 'Access denied'" sub-title="Your session may have expired or is not authorized for this operation.">
          <template #extra><a-button type="primary" @click="signIn('/system/status')">Sign in again</a-button></template>
        </a-result>
        <a-result v-else status="warning" title="Service unavailable" sub-title="Please try again later.">
          <template #extra><a-button type="primary" @click="refresh">Retry</a-button></template>
        </a-result>
        <p v-if="status.errorCode || status.correlationId" class="diagnostic" aria-live="polite">
          <span v-if="status.errorCode">Error code: {{ status.errorCode }}</span>
          <span v-if="status.correlationId">Correlation ID: {{ status.correlationId }}</span>
        </p>
      </template>
    </a-card>
  </main>
</template>

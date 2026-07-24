<script setup lang="ts">
import { useRoute } from 'vue-router'
import { authSnapshot, runtimeConfig, signIn } from '../auth-store'

const route = useRoute()
</script>

<template>
  <main class="hero" aria-labelledby="page-title">
    <a-card :bordered="false" class="hero-card">
      <p class="eyebrow">SYSTEM SHELL</p>
      <h1 id="page-title">OpenLIMS</h1>
      <p>This technical shell contains no LIMS business data or navigation.</p>
      <a-alert v-if="authSnapshot.status === 'configuration-error'" type="error" show-icon message="Configuration error" description="Sign-in is unavailable until the protected runtime configuration is completed." role="alert" />
      <a-spin v-else-if="authSnapshot.status === 'loading'" tip="Restoring secure session…" />
      <a-alert v-else-if="authSnapshot.status === 'expired'" type="warning" show-icon message="Session expired" description="Sign in again to continue." role="status">
        <template #action><a-button size="small" @click="signIn(route.fullPath)">Sign in</a-button></template>
      </a-alert>
      <a-result v-else-if="authSnapshot.status === 'anonymous'" status="info" title="Sign in required" sub-title="Use your organization’s configured identity provider to access this technical shell.">
        <template #extra><a-button type="primary" @click="signIn(route.fullPath)">Sign in</a-button></template>
      </a-result>
      <a-descriptions v-else-if="authSnapshot.status === 'authenticated' && runtimeConfig" :column="1" size="small" title="Session restored">
        <a-descriptions-item label="Environment">{{ runtimeConfig.environmentLabel }}</a-descriptions-item>
      </a-descriptions>
    </a-card>
  </main>
</template>

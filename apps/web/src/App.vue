<script setup lang="ts">
import { RouterLink, RouterView, useRoute } from 'vue-router'
import { authSnapshot, signIn, signOut } from './auth-store'

const route = useRoute()
</script>

<template>
  <a-layout class="app-shell">
    <a-layout-header class="app-header">
      <RouterLink class="brand" to="/" aria-label="OpenLIMS home">OpenLIMS</RouterLink>
      <nav aria-label="System navigation"><RouterLink to="/system/status">System status</RouterLink></nav>
      <div class="session-actions">
        <a-button v-if="authSnapshot.status === 'anonymous' || authSnapshot.status === 'expired'" size="small" @click="signIn(route.fullPath)">Sign in</a-button>
        <a-button v-else-if="authSnapshot.status === 'authenticated'" size="small" @click="signOut">Sign out</a-button>
      </div>
    </a-layout-header>
    <a-layout-content class="app-content"><RouterView /></a-layout-content>
    <a-layout-footer class="app-footer">OpenLIMS · Technical shell</a-layout-footer>
  </a-layout>
</template>

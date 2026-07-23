<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { completeSignIn } from '../auth-store'

const router = useRouter()
const failure = ref(false)
onMounted(async () => {
  const result = await completeSignIn()
  if (result.status === 'authenticated') await router.replace(result.returnTo ?? '/')
  else failure.value = true
})
</script>

<template>
  <main class="status-page" aria-labelledby="callback-title">
    <a-card :bordered="false">
      <h1 id="callback-title" class="sr-only">Sign-in callback</h1>
      <a-result v-if="failure" status="error" title="Sign-in could not be completed" sub-title="The callback was rejected or has expired. Return home and try again." />
      <a-spin v-else tip="Completing secure sign-in…" />
    </a-card>
  </main>
</template>

<script setup lang="ts">
import { useRoute } from 'vue-router'
import { signIn } from '../../auth-store'
import type { AuthStatus } from '../../auth'

defineProps<{
  status: AuthStatus
  canWrite: boolean
  capability: string
}>()

const route = useRoute()
</script>

<template>
  <div v-if="status === 'loading'" class="lab-notice" role="status">正在恢复安全会话…</div>
  <div v-else-if="status === 'configuration-error' || status === 'callback-error'" class="lab-notice lab-notice-error" role="alert">
    身份认证配置不可用，请联系系统支持人员。
  </div>
  <div v-else-if="status === 'anonymous' || status === 'expired'" class="lab-notice" role="alert">
    <p>请先登录后再读取或提交实验室业务数据。</p>
    <button type="button" @click="signIn(route.fullPath)">登录</button>
  </div>
  <div v-else-if="!canWrite" class="lab-notice lab-notice-warning" role="status">
    当前身份没有 <code>{{ capability }}</code> 能力；可以读取对象，但写操作已禁用。服务器仍会执行最终授权。
  </div>
</template>

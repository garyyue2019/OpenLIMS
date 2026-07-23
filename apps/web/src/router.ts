import { createRouter, createWebHistory } from 'vue-router'
import HomeView from './views/HomeView.vue'
import SystemStatusView from './views/SystemStatusView.vue'
import AuthCallbackView from './views/AuthCallbackView.vue'

export const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/', component: HomeView },
    { path: '/system/status', component: SystemStatusView },
    { path: '/auth/callback', component: AuthCallbackView }
  ]
})

import { createRouter, createWebHistory } from 'vue-router'
import { webFeatureComposition } from './web-feature-registry'

export const router = createRouter({
  history: createWebHistory(),
  routes: [...webFeatureComposition.routes]
})

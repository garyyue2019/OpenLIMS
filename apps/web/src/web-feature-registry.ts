import AuthCallbackView from './views/AuthCallbackView.vue'
import HomeView from './views/HomeView.vue'
import SystemStatusView from './views/SystemStatusView.vue'
import { receivingFeature } from './features/receiving/receiving-feature'
import {
  composeWebFeatures,
  type WebFeatureDescriptor
} from './web-feature'

export const platformShellFeature = {
  featureId: 'PLATFORM-SHELL',
  contractVersion: '1.0.0',
  routes: [
    { name: 'platform.home', path: '/', component: HomeView },
    { name: 'platform.system-status', path: '/system/status', component: SystemStatusView },
    { name: 'platform.auth-callback', path: '/auth/callback', component: AuthCallbackView }
  ],
  navigationEntries: [
    { id: 'platform.system-status', label: 'System status', routeName: 'platform.system-status' }
  ]
} as const satisfies WebFeatureDescriptor

// Production features are registered explicitly at build time. DEV-003 adds only
// the approved receiving registration slice; no runtime feature discovery is used.
export const webFeatureRegistry: readonly WebFeatureDescriptor[] = [
  platformShellFeature,
  receivingFeature
]

export const webFeatureComposition = composeWebFeatures(webFeatureRegistry)

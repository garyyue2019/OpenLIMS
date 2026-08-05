import AuthCallbackView from './views/AuthCallbackView.vue'
import HomeView from './views/HomeView.vue'
import SystemStatusView from './views/SystemStatusView.vue'
import { receivingFeature } from './features/receiving/receiving-feature'
import { labWorkbenchFeature } from './features/lab-workbench/lab-workbench-feature'
import { labWorkbenchSecondFeature } from './features/lab-workbench/lab-workbench-second-feature'
import { businessWorkbenchFeature } from './features/business-workbench/business-workbench-feature'
import { textileWorkbenchFeature } from './features/industry-workbench/textile-workbench-feature'
import { toyWorkbenchFeature } from './features/toy-workbench/toy-workbench-feature'
import { releaseWorkbenchFeature } from './features/release-workbench/release-workbench-feature'
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

// Production features are registered explicitly at build time. Receiving and both
// approved laboratory workbench slices are composed here; no runtime discovery is used.
export const webFeatureRegistry: readonly WebFeatureDescriptor[] = [
  platformShellFeature,
  receivingFeature,
  labWorkbenchFeature,
  labWorkbenchSecondFeature,
  businessWorkbenchFeature,
  textileWorkbenchFeature,
  toyWorkbenchFeature,
  releaseWorkbenchFeature
]

export const webFeatureComposition = composeWebFeatures(webFeatureRegistry)

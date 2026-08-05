import type { WebFeatureDescriptor } from '../../web-feature'
import TextileWorkbenchView from './TextileWorkbenchView.vue'

export const textileWorkbenchFeature = {
  featureId: 'TEXTILE-WORKBENCH',
  contractVersion: '1.0.0',
  routes: [
    { name: 'workbench.textile', path: '/workbench/textile', component: TextileWorkbenchView }
  ],
  navigationEntries: [
    { id: 'workbench.textile', label: '纺织裁样', routeName: 'workbench.textile' }
  ]
} as const satisfies WebFeatureDescriptor

import type { WebFeatureDescriptor } from '../../web-feature'
import AiReviewWorkbenchView from './AiReviewWorkbenchView.vue'
import CommercialWorkbenchView from './CommercialWorkbenchView.vue'
import OperationsWorkbenchView from './OperationsWorkbenchView.vue'

export const releaseWorkbenchFeature = {
  featureId: 'RELEASE-WORKBENCH',
  contractVersion: '1.0.0',
  routes: [
    { name: 'workbench.commercial', path: '/workbench/commercial', component: CommercialWorkbenchView },
    { name: 'workbench.operations', path: '/workbench/operations', component: OperationsWorkbenchView },
    { name: 'workbench.ai-review', path: '/workbench/ai-review', component: AiReviewWorkbenchView }
  ],
  navigationEntries: [
    { id: 'workbench.commercial', label: '商业受理', routeName: 'workbench.commercial' },
    { id: 'workbench.operations', label: '样品作业', routeName: 'workbench.operations' },
    { id: 'workbench.ai-review', label: 'AI 复核', routeName: 'workbench.ai-review' }
  ]
} as const satisfies WebFeatureDescriptor

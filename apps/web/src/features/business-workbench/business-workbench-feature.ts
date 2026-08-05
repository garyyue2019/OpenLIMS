import type { WebFeatureDescriptor } from '../../web-feature'
import BillingWorkbenchView from './BillingWorkbenchView.vue'
import LabelingWorkbenchView from './LabelingWorkbenchView.vue'

export const businessWorkbenchFeature = {
  featureId: 'BUSINESS-WORKBENCH-BILLING-LABELING',
  contractVersion: '1.0.0',
  routes: [
    { name: 'workbench.billing', path: '/workbench/billing', component: BillingWorkbenchView },
    { name: 'workbench.labeling', path: '/workbench/labeling', component: LabelingWorkbenchView }
  ],
  navigationEntries: [
    { id: 'workbench.billing', label: '计费证据', routeName: 'workbench.billing' },
    { id: 'workbench.labeling', label: '标签中心', routeName: 'workbench.labeling' }
  ]
} as const satisfies WebFeatureDescriptor

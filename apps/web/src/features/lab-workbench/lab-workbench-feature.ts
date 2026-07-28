import type { WebFeatureDescriptor } from '../../web-feature'
import AllocationWorkbenchView from './AllocationWorkbenchView.vue'
import BatchWorkbenchView from './BatchWorkbenchView.vue'
import QuantityWorkbenchView from './QuantityWorkbenchView.vue'
import ScopeWorkbenchView from './ScopeWorkbenchView.vue'

export const labWorkbenchFeature = {
  featureId: 'LAB-WORKBENCH-CORE-FLOW',
  contractVersion: '1.0.0',
  routes: [
    { name: 'workbench.scope', path: '/workbench/scope', component: ScopeWorkbenchView },
    { name: 'workbench.quantity', path: '/workbench/quantity', component: QuantityWorkbenchView },
    { name: 'workbench.allocation', path: '/workbench/allocation', component: AllocationWorkbenchView },
    { name: 'workbench.batch', path: '/workbench/batch', component: BatchWorkbenchView }
  ],
  navigationEntries: [
    { id: 'workbench.scope', label: '范围矩阵', routeName: 'workbench.scope' },
    { id: 'workbench.quantity', label: '数量账', routeName: 'workbench.quantity' },
    { id: 'workbench.allocation', label: '样品分配', routeName: 'workbench.allocation' },
    { id: 'workbench.batch', label: '批次管理', routeName: 'workbench.batch' }
  ]
} as const satisfies WebFeatureDescriptor

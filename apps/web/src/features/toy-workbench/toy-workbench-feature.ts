import type { WebFeatureDescriptor } from '../../web-feature'
import ToyConclusionWorkbenchView from './ToyConclusionWorkbenchView.vue'
import ToyLabelReviewWorkbenchView from './ToyLabelReviewWorkbenchView.vue'
import ToyProductWorkbenchView from './ToyProductWorkbenchView.vue'
import ToyTestUnitWorkbenchView from './ToyTestUnitWorkbenchView.vue'

export const toyWorkbenchFeature = {
  featureId: 'TOY-FULL-FLOW-WORKBENCH',
  contractVersion: '1.0.0',
  routes: [
    { name: 'workbench.toy-product', path: '/workbench/toy/product', component: ToyProductWorkbenchView },
    { name: 'workbench.toy-test-units', path: '/workbench/toy/test-units', component: ToyTestUnitWorkbenchView },
    { name: 'workbench.toy-label-review', path: '/workbench/toy/label-review', component: ToyLabelReviewWorkbenchView },
    { name: 'workbench.toy-conclusions', path: '/workbench/toy/conclusions', component: ToyConclusionWorkbenchView }
  ],
  navigationEntries: [
    { id: 'workbench.toy-product', label: '玩具年龄与可及性', routeName: 'workbench.toy-product' },
    { id: 'workbench.toy-test-units', label: '玩具 TestUnit', routeName: 'workbench.toy-test-units' },
    { id: 'workbench.toy-label-review', label: '玩具标签审核', routeName: 'workbench.toy-label-review' },
    { id: 'workbench.toy-conclusions', label: '玩具结论', routeName: 'workbench.toy-conclusions' }
  ]
} as const satisfies WebFeatureDescriptor

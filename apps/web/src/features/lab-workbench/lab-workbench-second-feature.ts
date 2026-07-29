import type { WebFeatureDescriptor } from '../../web-feature'
import InstrumentWorkbenchView from './InstrumentWorkbenchView.vue'
import QcWorkbenchView from './QcWorkbenchView.vue'
import ReportWorkbenchView from './ReportWorkbenchView.vue'
import ResultWorkbenchView from './ResultWorkbenchView.vue'

export const labWorkbenchSecondFeature = {
  featureId: 'LAB-WORKBENCH-SECOND-FLOW',
  contractVersion: '1.0.0',
  routes: [
    { name: 'workbench.instrument', path: '/workbench/instrument', component: InstrumentWorkbenchView },
    { name: 'workbench.result', path: '/workbench/result', component: ResultWorkbenchView },
    { name: 'workbench.qc', path: '/workbench/qc', component: QcWorkbenchView },
    { name: 'workbench.report', path: '/workbench/report', component: ReportWorkbenchView }
  ],
  navigationEntries: [
    { id: 'workbench.instrument', label: '仪器导入', routeName: 'workbench.instrument' },
    { id: 'workbench.result', label: '结果采用', routeName: 'workbench.result' },
    { id: 'workbench.qc', label: 'QC 放行', routeName: 'workbench.qc' },
    { id: 'workbench.report', label: '报告签发', routeName: 'workbench.report' }
  ]
} as const satisfies WebFeatureDescriptor

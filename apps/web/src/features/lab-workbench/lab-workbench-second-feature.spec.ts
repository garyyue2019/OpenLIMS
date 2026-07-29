import { describe, expect, it } from 'vitest'
import { labWorkbenchSecondFeature } from './lab-workbench-second-feature'

describe('second laboratory workbench feature', () => {
  it('owns four stable routes and matching navigation entries in flow order', () => {
    expect(labWorkbenchSecondFeature).toMatchObject({
      featureId: 'LAB-WORKBENCH-SECOND-FLOW',
      contractVersion: '1.0.0'
    })
    expect(labWorkbenchSecondFeature.routes.map(route => [route.name, route.path])).toEqual([
      ['workbench.instrument', '/workbench/instrument'],
      ['workbench.result', '/workbench/result'],
      ['workbench.qc', '/workbench/qc'],
      ['workbench.report', '/workbench/report']
    ])
    expect(labWorkbenchSecondFeature.navigationEntries).toEqual([
      { id: 'workbench.instrument', label: '仪器导入', routeName: 'workbench.instrument' },
      { id: 'workbench.result', label: '结果采用', routeName: 'workbench.result' },
      { id: 'workbench.qc', label: 'QC 放行', routeName: 'workbench.qc' },
      { id: 'workbench.report', label: '报告签发', routeName: 'workbench.report' }
    ])
  })
})

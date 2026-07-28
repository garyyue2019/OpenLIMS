import { describe, expect, it } from 'vitest'
import { labWorkbenchFeature } from './lab-workbench-feature'

describe('lab workbench feature', () => {
  it('owns four stable routes and matching navigation entries', () => {
    expect(labWorkbenchFeature).toMatchObject({
      featureId: 'LAB-WORKBENCH-CORE-FLOW',
      contractVersion: '1.0.0'
    })
    expect(labWorkbenchFeature.routes.map(route => [route.name, route.path])).toEqual([
      ['workbench.scope', '/workbench/scope'],
      ['workbench.quantity', '/workbench/quantity'],
      ['workbench.allocation', '/workbench/allocation'],
      ['workbench.batch', '/workbench/batch']
    ])
    expect(labWorkbenchFeature.navigationEntries).toEqual([
      { id: 'workbench.scope', label: '范围矩阵', routeName: 'workbench.scope' },
      { id: 'workbench.quantity', label: '数量账', routeName: 'workbench.quantity' },
      { id: 'workbench.allocation', label: '样品分配', routeName: 'workbench.allocation' },
      { id: 'workbench.batch', label: '批次管理', routeName: 'workbench.batch' }
    ])
  })
})

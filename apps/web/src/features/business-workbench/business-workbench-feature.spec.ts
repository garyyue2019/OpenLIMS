import { describe, expect, it } from 'vitest'
import { businessWorkbenchFeature } from './business-workbench-feature'

describe('Billing and Labeling business workbench feature', () => {
  it('owns two stable routes and matching navigation entries in business-flow order', () => {
    expect(businessWorkbenchFeature).toMatchObject({
      featureId: 'BUSINESS-WORKBENCH-BILLING-LABELING',
      contractVersion: '1.0.0'
    })
    expect(businessWorkbenchFeature.routes.map(route => [route.name, route.path])).toEqual([
      ['workbench.billing', '/workbench/billing'],
      ['workbench.labeling', '/workbench/labeling']
    ])
    expect(businessWorkbenchFeature.navigationEntries).toEqual([
      { id: 'workbench.billing', label: '计费证据', routeName: 'workbench.billing' },
      { id: 'workbench.labeling', label: '标签中心', routeName: 'workbench.labeling' }
    ])
  })
})

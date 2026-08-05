import { describe, expect, it } from 'vitest'
import { textileWorkbenchFeature } from './textile-workbench-feature'

describe('Textile workbench feature', () => {
  it('owns one stable route and matching navigation entry', () => {
    expect(textileWorkbenchFeature).toMatchObject({
      featureId: 'TEXTILE-WORKBENCH',
      contractVersion: '1.0.0'
    })
    expect(textileWorkbenchFeature.routes.map(route => [route.name, route.path])).toEqual([
      ['workbench.textile', '/workbench/textile']
    ])
    expect(textileWorkbenchFeature.navigationEntries).toEqual([
      { id: 'workbench.textile', label: '纺织裁样', routeName: 'workbench.textile' }
    ])
  })
})

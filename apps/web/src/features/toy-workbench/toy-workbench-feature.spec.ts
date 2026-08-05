import { describe, expect, it } from 'vitest'
import { toyWorkbenchFeature } from './toy-workbench-feature'

describe('Toy workbench feature', () => {
  it('owns four stable routes with matching navigation entries', () => {
    expect(toyWorkbenchFeature).toMatchObject({
      featureId: 'TOY-FULL-FLOW-WORKBENCH', contractVersion: '1.0.0'
    })
    expect(toyWorkbenchFeature.routes.map(route => [route.name, route.path])).toEqual([
      ['workbench.toy-product', '/workbench/toy/product'],
      ['workbench.toy-test-units', '/workbench/toy/test-units'],
      ['workbench.toy-label-review', '/workbench/toy/label-review'],
      ['workbench.toy-conclusions', '/workbench/toy/conclusions']
    ])
    expect(toyWorkbenchFeature.navigationEntries).toEqual([
      { id: 'workbench.toy-product', label: '玩具年龄与可及性', routeName: 'workbench.toy-product' },
      { id: 'workbench.toy-test-units', label: '玩具 TestUnit', routeName: 'workbench.toy-test-units' },
      { id: 'workbench.toy-label-review', label: '玩具标签审核', routeName: 'workbench.toy-label-review' },
      { id: 'workbench.toy-conclusions', label: '玩具结论', routeName: 'workbench.toy-conclusions' }
    ])
  })
})

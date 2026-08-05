import { describe, expect, it } from 'vitest'
import { releaseWorkbenchFeature } from './release-workbench-feature'

describe('release workbench feature', () => {
  it('registers the Commercial, Operations, and AI routes and navigation exactly', () => {
    expect(releaseWorkbenchFeature.contractVersion).toBe('1.0.0')
    expect(releaseWorkbenchFeature.routes.map(route => [route.name, route.path])).toEqual([
      ['workbench.commercial', '/workbench/commercial'],
      ['workbench.operations', '/workbench/operations'],
      ['workbench.ai-review', '/workbench/ai-review']
    ])
    expect(releaseWorkbenchFeature.navigationEntries).toEqual([
      { id: 'workbench.commercial', label: '商业受理', routeName: 'workbench.commercial' },
      { id: 'workbench.operations', label: '样品作业', routeName: 'workbench.operations' },
      { id: 'workbench.ai-review', label: 'AI 复核', routeName: 'workbench.ai-review' }
    ])
  })
})

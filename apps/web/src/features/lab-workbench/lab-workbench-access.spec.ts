import { describe, expect, it } from 'vitest'
import { hasLabCapability } from './lab-workbench-access'

describe('hasLabCapability', () => {
  it('accepts an exact string or array claim', () => {
    expect(hasLabCapability({ capability: 'scope.approve' }, 'scope.approve')).toBe(true)
    expect(hasLabCapability({ capability: ['quantity.post', 'batch.manage'] }, 'batch.manage')).toBe(true)
  })

  it('does not invent system-admin or substring access', () => {
    expect(hasLabCapability({ capability: 'system_admin' }, 'scope.approve')).toBe(false)
    expect(hasLabCapability({ capability: 'scope.approve.all' }, 'scope.approve')).toBe(false)
    expect(hasLabCapability(undefined, 'scope.approve')).toBe(false)
  })
})

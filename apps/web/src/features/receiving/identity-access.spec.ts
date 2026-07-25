import { describe, expect, it } from 'vitest'
import { hasIdentityEvaluateCapability } from './identity-access'

describe('identity assessment access', () => {
  it('requires the exact business capability and never expands system admin', () => {
    expect(hasIdentityEvaluateCapability({ capability: 'receiving.identity.evaluate' })).toBe(true)
    expect(hasIdentityEvaluateCapability({ capability: ['receiving.identity.evaluate'] })).toBe(true)
    expect(hasIdentityEvaluateCapability({ capability: ['system_admin'] })).toBe(false)
    expect(hasIdentityEvaluateCapability(undefined)).toBe(false)
  })
})

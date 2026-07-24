import { describe, expect, it } from 'vitest'
import { hasReceivingRegisterCapability } from './receiving-access'

describe('receiving capability presentation', () => {
  it('accepts only the exact receiving.register claim', () => {
    expect(hasReceivingRegisterCapability({ capability: 'receiving.register' })).toBe(true)
    expect(hasReceivingRegisterCapability({ capability: ['other', 'receiving.register'] })).toBe(true)
    expect(hasReceivingRegisterCapability({ capability: 'system_admin' })).toBe(false)
    expect(hasReceivingRegisterCapability({ capability: 'receiving.*' })).toBe(false)
    expect(hasReceivingRegisterCapability(undefined)).toBe(false)
  })
})

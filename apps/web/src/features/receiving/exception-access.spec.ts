import { describe, expect, it } from 'vitest'
import { canCreateException, canEhsApproveException, canQualityApproveException, canReadException } from './exception-access'

describe('receiving exception access', () => {
  it('requires each exact exception capability', () => {
    const profile = { capability: ['exception.create', 'exception.read', 'exception.quality.approve'] }
    expect(canCreateException(profile)).toBe(true)
    expect(canReadException(profile)).toBe(true)
    expect(canQualityApproveException(profile)).toBe(true)
    expect(canEhsApproveException(profile)).toBe(false)
  })
})

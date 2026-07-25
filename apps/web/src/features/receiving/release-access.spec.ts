import { describe, expect, it } from 'vitest'
import { canApproveReceivingRelease } from './release-access'

describe('receiving release capability presentation', () => {
  it('requires the exact quality release capability', () => {
    expect(canApproveReceivingRelease({ capability: 'receiving.release.approve' })).toBe(true)
    expect(canApproveReceivingRelease({ capability: ['receiving.release.approve'] })).toBe(true)
    expect(canApproveReceivingRelease({ capability: ['exception.quality.approve'] })).toBe(false)
    expect(canApproveReceivingRelease({ capability: ['system_admin'] })).toBe(false)
  })
})

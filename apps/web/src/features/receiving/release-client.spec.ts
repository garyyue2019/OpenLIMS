import { describe, expect, it, vi } from 'vitest'
import {
  ReceivingReleaseApiError,
  releaseRuleSetVersion,
  submitReceivingReleaseDecision
} from './release-client'

describe('receiving release client', () => {
  it('posts the pinned release request with bearer authorization', async () => {
    const fetcher = vi.fn().mockResolvedValue(new Response(JSON.stringify({
      releaseDecisionId: 'release-a', outcome: 'RELEASED', state: 'ACCEPTED'
    }), { status: 201, headers: { 'Content-Type': 'application/json' } }))

    await submitReceivingReleaseDecision('item/a', {
      expectedItemVersion: 3,
      ruleSetVersion: releaseRuleSetVersion,
      rationale: 'Quality review complete.'
    }, 'token-a', fetcher)

    expect(fetcher).toHaveBeenCalledWith('/api/v1/received-items/item%2Fa/release-decisions', expect.objectContaining({
      method: 'POST',
      headers: { Authorization: 'Bearer token-a', 'Content-Type': 'application/json' }
    }))
  })

  it('preserves the server error code', async () => {
    const fetcher = vi.fn().mockResolvedValue(new Response(JSON.stringify({ errorCode: 'BLOCKING_EXCEPTION' }), {
      status: 422,
      headers: { 'Content-Type': 'application/json' }
    }))

    await expect(submitReceivingReleaseDecision('item-a', {
      expectedItemVersion: 3,
      ruleSetVersion: releaseRuleSetVersion,
      rationale: 'Quality review complete.'
    }, 'token-a', fetcher)).rejects.toEqual(new ReceivingReleaseApiError('BLOCKING_EXCEPTION', 422))
  })
})

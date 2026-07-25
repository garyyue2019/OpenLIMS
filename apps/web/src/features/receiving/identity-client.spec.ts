import { describe, expect, it, vi } from 'vitest'
import {
  createIdentityObservation,
  getIdentityAssessment,
  IdentityApiError,
  identityRuleSetVersion,
  submitIdentityDecision
} from './identity-client'

describe('identity assessment client', () => {
  it('uses exact item paths and bearer token without a group selector', async () => {
    const fetcher = vi.fn<typeof fetch>().mockImplementation(() => Promise.resolve(okResponse()))

    await getIdentityAssessment('item/a', 'token-a', fetcher)
    await createIdentityObservation('item-a', {
      expectedItemVersion: 1,
      observedLabels: ['LABEL-01'],
      observedModel: 'MODEL-001',
      observedBatch: 'BATCH-001',
      appearance: 'intact',
      attachmentRefs: ['object://photo'],
      attachmentHashes: ['a'.repeat(64)]
    }, 'token-a', fetcher)

    expect(fetcher.mock.calls[0][0]).toBe('/api/v1/received-items/item%2Fa/identity-assessment')
    expect(fetcher.mock.calls[0][1]?.headers).toMatchObject({ Authorization: 'Bearer token-a' })
    expect(fetcher.mock.calls[1][0]).toBe('/api/v1/received-items/item-a/identity-observations')
    expect(fetcher.mock.calls[1][1]?.body).not.toContain('organizationGroupId')
  })

  it('binds a manual conclusion to exact evidence and rule versions', async () => {
    const fetcher = vi.fn<typeof fetch>().mockResolvedValue(okResponse())

    await submitIdentityDecision('item-a', {
      expectedItemVersion: 2,
      observationVersion: 1,
      declarationSnapshotVersion: 1,
      outcome: 'MATCHED',
      reasonCode: 'CONSISTENT',
      rationale: 'Reviewed evidence is consistent.',
      ruleSetVersion: identityRuleSetVersion
    }, 'token-a', fetcher)

    expect(fetcher.mock.calls[0][0]).toBe('/api/v1/received-items/item-a/identity-decisions')
    expect(fetcher.mock.calls[0][1]?.body).toContain('REC-ELIGIBILITY@1.0.0')
  })

  it('keeps stable server error codes', async () => {
    const fetcher = vi.fn<typeof fetch>().mockResolvedValue(new Response(JSON.stringify({
      errorCode: 'EXPECTED_VERSION_CONFLICT'
    }), { status: 409, headers: { 'Content-Type': 'application/json' } }))

    await expect(getIdentityAssessment('item-a', 'token-a', fetcher))
      .rejects.toEqual(new IdentityApiError('EXPECTED_VERSION_CONFLICT', 409))
  })
})

function okResponse(): Response {
  return new Response(JSON.stringify({
    receivedItemId: 'item-a',
    receivedItemNumber: 'ITM-A',
    currentState: 'QUARANTINED',
    itemVersion: 1,
    assessmentState: 'NOT_STARTED',
    assessmentVersion: 0,
    observations: [],
    decisions: []
  }), { status: 200, headers: { 'Content-Type': 'application/json' } })
}

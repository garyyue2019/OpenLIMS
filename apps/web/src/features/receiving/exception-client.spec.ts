import { describe, expect, it, vi } from 'vitest'
import {
  createReceivingException,
  exceptionMatrixVersion,
  getReceivingException,
  ReceivingExceptionApiError,
  submitReceivingExceptionDecision
} from './exception-client'

describe('receiving exception client', () => {
  it('uses exact paths, bearer token, and no client-selectable group', async () => {
    const fetcher = vi.fn<typeof fetch>().mockImplementation(() => Promise.resolve(okResponse()))
    await createReceivingException({
      receivedItemId: 'item-a', expectedItemVersion: 3, type: 'DAMAGED',
      observedAt: '2026-07-25T10:00:00Z', description: 'damaged',
      evidenceRefs: ['object://evidence'], evidenceHashes: ['a'.repeat(64)]
    }, 'token-a', fetcher)
    await getReceivingException('exception/a', 'token-a', fetcher)

    expect(fetcher.mock.calls[0][0]).toBe('/api/v1/exceptions')
    expect(fetcher.mock.calls[0][1]?.headers).toMatchObject({ Authorization: 'Bearer token-a' })
    expect(fetcher.mock.calls[0][1]?.body).not.toContain('organizationGroupId')
    expect(fetcher.mock.calls[1][0]).toBe('/api/v1/exceptions/exception%2Fa')
  })

  it('binds a decision to exact exception and matrix versions', async () => {
    const fetcher = vi.fn<typeof fetch>().mockResolvedValue(okResponse())
    await submitReceivingExceptionDecision('exception-a', {
      expectedVersion: 1, decisionType: 'REJECT', allowedActions: [], prohibitedActions: [],
      evidenceRefs: ['object://decision'], evidenceHashes: ['b'.repeat(64)],
      technicalImpact: '', rationale: 'Rejected.', matrixVersion: exceptionMatrixVersion
    }, 'token-a', fetcher)
    expect(fetcher.mock.calls[0][0]).toBe('/api/v1/exceptions/exception-a/decisions')
    expect(fetcher.mock.calls[0][1]?.body).toContain('OD-005@1.0.0')
  })

  it('keeps stable server error codes', async () => {
    const fetcher = vi.fn<typeof fetch>().mockResolvedValue(new Response(JSON.stringify({
      errorCode: 'DECISION_NOT_AUTHORIZED'
    }), { status: 403, headers: { 'Content-Type': 'application/json' } }))
    await expect(getReceivingException('exception-a', 'token-a', fetcher))
      .rejects.toEqual(new ReceivingExceptionApiError('DECISION_NOT_AUTHORIZED', 403))
  })
})

function okResponse(): Response {
  return new Response(JSON.stringify({
    exceptionId: 'exception-a', receivedItemId: 'item-a', receivedItemNumber: 'ITM-A',
    itemVersion: 4, itemState: 'QUARANTINED', type: 'DAMAGED', severity: 'STANDARD',
    description: 'damaged', observedAt: '2026-07-25T10:00:00Z', evidenceRefs: ['object://evidence'],
    evidenceHashes: ['a'.repeat(64)], createdBy: 'creator', createdAt: '2026-07-25T10:00:00Z',
    status: 'OPEN', version: 1, decisions: []
  }), { status: 200, headers: { 'Content-Type': 'application/json' } })
}

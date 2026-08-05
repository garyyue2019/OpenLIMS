import { describe, expect, it, vi } from 'vitest'
import {
  createLabelJobs,
  getLabelJob,
  LabelingApiError,
  resolveLabelScan
} from './labeling-client'

describe('labeling client', () => {
  it('creates a batch with bearer token and idempotency but no group selector', async () => {
    const fetcher = vi.fn<typeof fetch>().mockResolvedValue(new Response(JSON.stringify({ jobs: [] }), {
      status: 202,
      headers: { 'Content-Type': 'application/json' }
    }))

    await createLabelJobs({
      printerId: 'printer-a',
      targets: [{ objectType: 'RI', objectId: 'item-a', objectVersion: 1 }]
    }, 'token-a', 'idem-a', fetcher)

    const [path, init] = fetcher.mock.calls[0]
    expect(path).toBe('/api/v1/label-jobs')
    expect(init?.headers).toMatchObject({ Authorization: 'Bearer token-a', 'Idempotency-Key': 'idem-a' })
    expect(init?.body).not.toContain('organizationGroupId')
  })

  it('submits keyboard-scanner payload to server-side resolution', async () => {
    const fetcher = vi.fn<typeof fetch>().mockResolvedValue(new Response(JSON.stringify({
      objectType: 'RI', objectId: 'item-a', businessNumber: 'LAB-A-RI-20260724-000001',
      state: 'QUARANTINED', printVerificationStatus: 'VERIFIED', allowedActions: []
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }))

    const result = await resolveLabelScan('OL1:RI:token:checksum', 'token-a', fetcher)

    expect(result.objectType).toBe('RI')
    expect(fetcher.mock.calls[0][1]?.body).toBe(JSON.stringify({ barcodePayload: 'OL1:RI:token:checksum' }))
  })

  it('keeps stable server error codes', async () => {
    const fetcher = vi.fn<typeof fetch>().mockResolvedValue(new Response(JSON.stringify({
      errorCode: 'LABEL.PRINTER_SCOPE_MISMATCH'
    }), { status: 403, headers: { 'Content-Type': 'application/json' } }))

    await expect(createLabelJobs({ printerId: 'wrong', targets: [] }, 'token', 'idem', fetcher))
      .rejects.toMatchObject({
        errorCode: 'LABEL.PRINTER_SCOPE_MISMATCH',
        status: 403
      } satisfies Partial<LabelingApiError>)
  })

  it('preserves problem correlation and explicit network recovery metadata', async () => {
    const problemFetcher = vi.fn<typeof fetch>().mockResolvedValue(new Response(JSON.stringify({
      errorCode: 'LABEL.PRINT_DELIVERY_UNKNOWN',
      correlationId: 'corr-server',
      detail: 'Delivery is unknown.',
      nextAction: 'Scan or perform a controlled reprint.'
    }), { status: 422, headers: { 'Content-Type': 'application/json' } }))

    await expect(getLabelJob('job-a', 'token', problemFetcher)).rejects.toMatchObject({
      errorCode: 'LABEL.PRINT_DELIVERY_UNKNOWN',
      correlationId: 'corr-server',
      detail: 'Delivery is unknown.',
      retryable: false
    })

    const networkFetcher = vi.fn<typeof fetch>().mockRejectedValue(new Error('offline'))
    await expect(resolveLabelScan('payload', 'token', networkFetcher)).rejects.toMatchObject({
      errorCode: 'WEB.NETWORK_ERROR',
      status: 0,
      retryable: true
    })
  })
})

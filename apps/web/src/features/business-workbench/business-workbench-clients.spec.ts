import { describe, expect, it, vi } from 'vitest'
import {
  addBillingAdjustment,
  BILLING_EXPORT_RULE_SET_VERSION,
  BILLING_HANDOFF_RULE_SET_VERSION,
  BILLING_RULE_SET_VERSION,
  createBillingExportBatch,
  createBillingEvidence,
  createBillingHandoff,
  getBillingDifferenceQueue,
  getBillingEvidence,
  getBillingEvidenceStatus,
  getBillingExportBatch,
  getBillingHandoff,
  recordBillingHandoffAttempt,
  type CreateBillingExportBatchRequest,
  type CreateBillingHandoffRequest,
  type RecordBillingHandoffAttemptRequest
} from './billing-client'
import {
  createLabelJobs,
  getLabelJob,
  reprintLabel,
  resolveLabelScan
} from '../receiving/labeling-client'

const context = { accessToken: 'token', correlationId: 'corr' }

describe('Billing and Labeling typed clients', () => {
  it('covers Billing evidence, export, and handoff operations with exact bodies', async () => {
    const fetcher = successFetcher()
    await createBillingEvidence({
      ruleSetVersion: BILLING_RULE_SET_VERSION,
      objectScope: {
        legalEntityId: 'legal', laboratoryId: 'lab', customerId: 'customer',
        serviceOrderId: 'order', productCategory: 'TOYS'
      },
      resultGroupId: 'result-group', expectedGroupVersion: 2,
      contractBaseline: { id: 'contract', version: 3 }, chargeDimension: 'TEST',
      billingRuleVersion: 'PRICE@1.0.0', amount: 12.5,
      currency: { id: 'CNY', version: 1 }
    }, { ...context, fetcher })
    await addBillingAdjustment('evidence/1', {
      ruleSetVersion: BILLING_RULE_SET_VERSION, amount: -2, reason: 'credit'
    }, { ...context, fetcher })
    await getBillingEvidence('evidence/1', { ...context, fetcher })
    await getBillingEvidenceStatus('evidence/1', { ...context, fetcher })
    const exportBatch: CreateBillingExportBatchRequest = {
      ruleSetVersion: BILLING_EXPORT_RULE_SET_VERSION,
      billingEvidenceIds: ['evidence-1'], exportSchemaVersion: 'ERP-SCHEMA@1.0.0',
      idempotencyKey: 'export-1'
    }
    await createBillingExportBatch(exportBatch, { ...context, fetcher })
    await getBillingExportBatch('batch/1', { ...context, fetcher })
    const handoff: CreateBillingHandoffRequest = {
      ruleSetVersion: BILLING_HANDOFF_RULE_SET_VERSION, externalSystem: 'ERP',
      mode: 'MANUAL', endpoint: { id: 'erp-endpoint', version: 1 },
      idempotencyKey: 'handoff-1'
    }
    await createBillingHandoff('batch/1', handoff, { ...context, fetcher })
    await getBillingHandoff('handoff/1', { ...context, fetcher })
    const attempt: RecordBillingHandoffAttemptRequest = {
      ruleSetVersion: BILLING_HANDOFF_RULE_SET_VERSION, idempotencyKey: 'attempt-1',
      outcome: 'DIFFERENT', externalReference: 'ERP-REF-1', detailCode: 'AMOUNT_MISMATCH'
    }
    await recordBillingHandoffAttempt('handoff/1', attempt, { ...context, fetcher })
    await getBillingDifferenceQueue('ERP', { ...context, fetcher })

    expect(paths(fetcher)).toEqual([
      '/api/v1/billing-evidence',
      '/api/v1/billing-evidence/evidence%2F1/adjustments',
      '/api/v1/billing-evidence/evidence%2F1',
      `/api/v1/billing-evidence/evidence%2F1/status?ruleSetVersion=${encodeURIComponent(BILLING_RULE_SET_VERSION)}`,
      '/api/v1/billing-export-batches',
      '/api/v1/billing-export-batches/batch%2F1',
      '/api/v1/billing-export-batches/batch%2F1/handoffs',
      '/api/v1/billing-handoffs/handoff%2F1',
      '/api/v1/billing-handoffs/handoff%2F1/attempts',
      '/api/v1/billing-handoffs/differences?externalSystem=ERP'
    ])
    expect(methods(fetcher)).toEqual(['POST', 'POST', 'GET', 'GET', 'POST', 'GET', 'POST', 'GET', 'POST', 'GET'])
    expect(JSON.parse(requestBodies(fetcher)[4]!)).toEqual(exportBatch)
    expect(JSON.parse(requestBodies(fetcher)[6]!)).toEqual(handoff)
    expect(JSON.parse(requestBodies(fetcher)[8]!)).toEqual(attempt)
    expect(requestBodies(fetcher).join('')).not.toContain('organizationGroupId')
    expect(requestBodies(fetcher).join('')).not.toContain('actorId')
  })

  it('covers all four Labeling operations with bearer, correlation, and write idempotency', async () => {
    const fetcher = successFetcher({ jobs: [] })
    await createLabelJobs({
      printerId: 'printer-a',
      targets: [{ objectType: 'RI', objectId: 'item-a', objectVersion: 1 }]
    }, 'token', 'idem-create', fetcher)
    await getLabelJob('job/1', 'token', fetcher)
    await reprintLabel('job/1', 'printer-a', 'damaged label', 'token', 'idem-reprint', fetcher)
    await resolveLabelScan('OL1:RI:opaque:checksum', 'token', fetcher)

    expect(paths(fetcher)).toEqual([
      '/api/v1/label-jobs',
      '/api/v1/label-jobs/job%2F1',
      '/api/v1/label-jobs/job%2F1/reprint',
      '/api/v1/scans/resolve'
    ])
    expect(methods(fetcher)).toEqual(['POST', 'GET', 'POST', 'POST'])
    expect(headers(fetcher)[0]).toMatchObject({
      Authorization: 'Bearer token',
      'Idempotency-Key': 'idem-create'
    })
    expect(headers(fetcher)[2]).toMatchObject({ 'Idempotency-Key': 'idem-reprint' })
    expect(headers(fetcher).every(header => typeof header['X-Correlation-Id'] === 'string')).toBe(true)
    expect(requestBodies(fetcher).join('')).not.toContain('organizationGroupId')
  })
})

function successFetcher(payload: unknown = {}) {
  return vi.fn(async () => new Response(JSON.stringify(payload), {
    status: 200, headers: { 'Content-Type': 'application/json' }
  })) as unknown as typeof fetch & { mock: { calls: [string, RequestInit][] } }
}

function paths(fetcher: ReturnType<typeof successFetcher>): string[] {
  return fetcher.mock.calls.map(call => call[0])
}

function methods(fetcher: ReturnType<typeof successFetcher>): string[] {
  return fetcher.mock.calls.map(call => String(call[1]?.method ?? 'GET'))
}

function headers(fetcher: ReturnType<typeof successFetcher>): Record<string, string>[] {
  return fetcher.mock.calls.map(call => call[1]?.headers as Record<string, string>)
}

function requestBodies(fetcher: ReturnType<typeof successFetcher>): string[] {
  return fetcher.mock.calls.map(call => String(call[1]?.body ?? ''))
}

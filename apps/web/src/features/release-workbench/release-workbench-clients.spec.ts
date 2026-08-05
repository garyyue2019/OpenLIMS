import { describe, expect, it, vi } from 'vitest'
import {
  createCatalogRecord,
  createInquiry,
  createQuoteVersion,
  getCatalogRecord,
  getInquiry,
  recordCapabilityReview,
  recordCommercialChangeImpact,
  resolveInquiryGap,
  reviseCatalogRecord
} from './commercial-client'
import {
  changeWorkTaskState,
  createLineageEdge,
  createWorkPlan,
  getCustodyChain,
  getSampleLineage,
  getWorkPlan,
  getWorkQueue,
  recordCustodyEvent,
  reserveWorkResource
} from './operations-client'
import {
  AI_RUNTIME_RULE_SET_VERSION,
  createAiRun,
  getAiReviewQueue,
  getAiRun,
  recordAiDisposition,
  type CreateAiRunRequest,
  type RecordAiDispositionRequest
} from './ai-client'

const context = { accessToken: 'token', correlationId: 'corr' }
const versionedRef = { id: 'ref-1', version: 1 }
const objectScope = {
  legalEntityId: 'legal', laboratoryId: 'lab', customerId: 'customer',
  serviceOrderId: 'order', productCategory: 'TOYS'
}

describe('Release workbench typed clients', () => {
  it('covers every Commercial route with exact encoded ids and request bodies', async () => {
    const fetcher = successFetcher()
    const catalogRequest = {
      expectedCurrentVersion: 0, kind: 'METHOD', code: 'M-1', displayName: 'Method one',
      validFrom: '2026-08-05T00:00:00Z', state: 'ACTIVE', attributes: { matrix: 'toy' },
      references: [versionedRef], objectScope
    }
    const inquiryRequest = {
      details: { customerName: 'Customer', productCategory: 'TOYS', quantity: 2, quantityUnit: 'EA', sourceDocuments: [versionedRef] },
      objectScope
    }
    const capabilityRequest = {
      expectedCurrentVersion: 2, methodCapabilityConfirmed: true, accreditationConfirmed: true,
      personnelAndEquipmentConfirmed: true, sampleQuantityConfirmed: true,
      turnaroundConfirmed: true, confidentialityConfirmed: true,
      evidence: [versionedRef], notes: 'reviewed'
    }
    const quoteRequest = {
      expectedInquiryVersion: 3, expectedQuoteVersion: 0, scopeMatrix: versionedRef,
      currency: versionedRef, contractReference: versionedRef, promisedTurnaroundDays: 5,
      exclusions: ['shipping'], lines: [{ lineCode: 'L1', description: 'Test', quantity: 1, unitPrice: 100 }]
    }

    await createCatalogRecord(catalogRequest, { ...context, fetcher })
    await reviseCatalogRecord('record/1', { ...catalogRequest, expectedCurrentVersion: 1 }, { ...context, fetcher })
    await getCatalogRecord('record/1', 2, { ...context, fetcher })
    await createInquiry(inquiryRequest, { ...context, fetcher })
    await getInquiry('inquiry/1', { ...context, fetcher })
    await resolveInquiryGap('inquiry/1', 'gap/1', { expectedCurrentVersion: 1, value: '2 EA' }, { ...context, fetcher })
    await recordCapabilityReview('inquiry/1', capabilityRequest, { ...context, fetcher })
    await createQuoteVersion('inquiry/1', quoteRequest, { ...context, fetcher })
    await recordCommercialChangeImpact('inquiry/1', {
      expectedInquiryVersion: 4, changeKind: 'SCOPE', reason: 'customer update'
    }, { ...context, fetcher })

    expect(paths(fetcher)).toEqual([
      '/api/v1/catalog-records',
      '/api/v1/catalog-records/record%2F1/versions',
      '/api/v1/catalog-records/record%2F1/versions/2',
      '/api/v1/inquiries',
      '/api/v1/inquiries/inquiry%2F1',
      '/api/v1/inquiries/inquiry%2F1/gaps/gap%2F1/resolution',
      '/api/v1/inquiries/inquiry%2F1/capability-reviews',
      '/api/v1/inquiries/inquiry%2F1/quote-versions',
      '/api/v1/inquiries/inquiry%2F1/change-impacts'
    ])
    expect(methods(fetcher)).toEqual(['POST', 'POST', 'GET', 'POST', 'GET', 'POST', 'POST', 'POST', 'POST'])
    expect(body(fetcher, 0)).toEqual(catalogRequest)
    expect(body(fetcher, 5)).toEqual({ expectedCurrentVersion: 1, value: '2 EA' })
    expect(body(fetcher, 6)).toEqual(capabilityRequest)
    expect(body(fetcher, 7)).toEqual(quoteRequest)
  })

  it('covers every Operations route including exact work queue filters', async () => {
    const fetcher = successFetcher()
    const workPlanRequest = {
      scopeMatrix: versionedRef, sampleIdentity: versionedRef,
      tasks: [{
        taskId: 'task-1', scopeLineId: 'line-1', method: versionedRef,
        workCenterId: 'center/1', priority: 5, sequence: 1, destructive: false,
        dependencyTaskIds: []
      }],
      objectScope
    }
    const reservationRequest = {
      expectedPlanVersion: 2, taskId: 'task-1', resourceKind: 'INSTRUMENT',
      resourceId: 'instrument-1', startsAt: '2026-08-05T01:00:00Z', endsAt: '2026-08-05T02:00:00Z'
    }

    await createLineageEdge({
      sourceObjectId: 'sample-a', targetObjectId: 'sample-b', relationKind: 'ALIQUOT',
      basis: versionedRef, objectScope
    }, { ...context, fetcher })
    await getSampleLineage('sample/1', { ...context, fetcher })
    await recordCustodyEvent({
      objectId: 'sample-1', eventKind: 'TRANSFER', fromLocationId: 'cold-room',
      toLocationId: 'bench', responsiblePartyId: 'analyst', evidenceRef: 'scan-1', objectScope
    }, { ...context, fetcher })
    await getCustodyChain('sample/1', { ...context, fetcher })
    await createWorkPlan(workPlanRequest, { ...context, fetcher })
    await getWorkPlan('plan/1', { ...context, fetcher })
    await changeWorkTaskState('plan/1', 'task/1', {
      expectedPlanVersion: 1, state: 'READY', reason: 'dependencies complete'
    }, { ...context, fetcher })
    await reserveWorkResource('plan/1', reservationRequest, { ...context, fetcher })
    await getWorkQueue('center/1', 'READY', { ...context, fetcher })

    expect(paths(fetcher)).toEqual([
      '/api/v1/sample-lineage/edges',
      '/api/v1/sample-lineage/sample%2F1',
      '/api/v1/custody-events',
      '/api/v1/samples/sample%2F1/custody',
      '/api/v1/work-plans',
      '/api/v1/work-plans/plan%2F1',
      '/api/v1/work-plans/plan%2F1/tasks/task%2F1/state',
      '/api/v1/work-plans/plan%2F1/resource-reservations',
      '/api/v1/work-queues?workCenterId=center%2F1&state=READY'
    ])
    expect(methods(fetcher)).toEqual(['POST', 'GET', 'POST', 'GET', 'POST', 'GET', 'POST', 'POST', 'GET'])
    expect(body(fetcher, 4)).toEqual(workPlanRequest)
    expect(body(fetcher, 6)).toEqual({ expectedPlanVersion: 1, state: 'READY', reason: 'dependencies complete' })
    expect(body(fetcher, 7)).toEqual(reservationRequest)
  })

  it('covers every AI runtime route without client-supplied reviewer identity', async () => {
    const fetcher = successFetcher()
    const runRequest: CreateAiRunRequest = {
      ruleSetVersion: AI_RUNTIME_RULE_SET_VERSION,
      objectScope,
      envelope: {
        model: versionedRef, gatewayRoute: 'manual-disabled', promptTemplate: versionedRef,
        outputSchema: versionedRef, inputRefs: [versionedRef]
      },
      validationProfile: versionedRef, allowedFields: ['sampleNumber'], allowedUnits: ['mg/kg'],
      idempotencyKey: 'ai-run-1'
    }
    const dispositionRequest: RecordAiDispositionRequest = {
      expectedRunVersion: 1, ruleSetVersion: AI_RUNTIME_RULE_SET_VERSION,
      candidateId: 'candidate-1', kind: 'MODIFY', reason: 'corrected from source',
      idempotencyKey: 'disposition-1', humanValue: 'S-100'
    }

    await createAiRun(runRequest, { ...context, fetcher })
    await getAiRun('run/1', { ...context, fetcher })
    await recordAiDisposition('run/1', dispositionRequest, { ...context, fetcher })
    await getAiReviewQueue('QUARANTINED', { ...context, fetcher })

    expect(paths(fetcher)).toEqual([
      '/api/v1/ai-runs',
      '/api/v1/ai-runs/run%2F1',
      '/api/v1/ai-runs/run%2F1/dispositions',
      '/api/v1/ai-review-queue?status=QUARANTINED'
    ])
    expect(methods(fetcher)).toEqual(['POST', 'GET', 'POST', 'GET'])
    expect(body(fetcher, 0)).toEqual(runRequest)
    expect(body(fetcher, 2)).toEqual(dispositionRequest)
    expect(JSON.stringify(body(fetcher, 2))).not.toContain('reviewedBy')
  })
})

function successFetcher() {
  return vi.fn(async () => new Response('{}', {
    status: 200, headers: { 'Content-Type': 'application/json' }
  })) as unknown as typeof fetch & { mock: { calls: [string, RequestInit][] } }
}

function paths(fetcher: ReturnType<typeof successFetcher>): string[] {
  return fetcher.mock.calls.map(call => call[0])
}

function methods(fetcher: ReturnType<typeof successFetcher>): string[] {
  return fetcher.mock.calls.map(call => String(call[1]?.method ?? 'GET'))
}

function body(fetcher: ReturnType<typeof successFetcher>, callIndex: number): unknown {
  return JSON.parse(String(fetcher.mock.calls[callIndex]?.[1]?.body))
}

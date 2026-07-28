import { describe, expect, it, vi } from 'vitest'
import {
  addBatchEvidence, addBatchMember, createBatch, freezeBatch, getBatch, getBatchStatus,
  BATCH_RULE_SET_VERSION
} from './batch-client'
import {
  createScopeMatrix, getScopeMatrixVersion, getScopeProductionEligibility, reviseScopeMatrix,
  SCOPE_RULE_SET_VERSION, type SubmitScopeMatrixVersionRequest
} from './scope-client'
import {
  createQuantityAccount, getQuantityAccount, getQuantityAvailability, postQuantityEntry,
  QUANTITY_RULE_SET_VERSION, type CreateQuantityAccountRequest
} from './quantity-client'
import {
  createTestObjectAllocation, getAllocationStatus, getTestObjectAllocation,
  releaseTestObjectAllocation, ALLOCATION_RULE_SET_VERSION,
  type CreateTestObjectAllocationRequest
} from './allocation-client'

const context = { accessToken: 'token', correlationId: 'corr' }

describe('laboratory typed clients', () => {
  it('uses the exact Scope paths, version pins, and rule set', async () => {
    const fetcher = successFetcher()
    const request = scopeRequest()
    await createScopeMatrix(request, { ...context, fetcher })
    await reviseScopeMatrix('matrix/1', { ...request, expectedCurrentVersion: 1 }, { ...context, fetcher })
    await getScopeMatrixVersion('matrix/1', 2, { ...context, fetcher })
    await getScopeProductionEligibility('matrix/1', 2, { ...context, fetcher })

    expect(paths(fetcher)).toEqual([
      '/api/v1/scope-matrices',
      '/api/v1/scope-matrices/matrix%2F1/versions',
      '/api/v1/scope-matrices/matrix%2F1/versions/2',
      `/api/v1/scope-matrices/matrix%2F1/production-eligibility?expectedVersion=2&ruleSetVersion=${encodeURIComponent(SCOPE_RULE_SET_VERSION)}`
    ])
  })

  it('uses every Quantity operation with an exact availability query', async () => {
    const fetcher = successFetcher()
    await createQuantityAccount(quantityRequest(), { ...context, fetcher })
    await postQuantityEntry('account-1', {
      expectedCurrentVersion: 1, ruleSetVersion: QUANTITY_RULE_SET_VERSION,
      entryType: 'RECEIPT', amount: 12.5
    }, { ...context, fetcher })
    await getQuantityAccount('account-1', { ...context, fetcher })
    await getQuantityAvailability('account-1', 2, 3.5, { ...context, fetcher })

    expect(paths(fetcher)).toEqual([
      '/api/v1/quantity-accounts',
      '/api/v1/quantity-accounts/account-1/entries',
      '/api/v1/quantity-accounts/account-1',
      `/api/v1/quantity-accounts/account-1/availability?expectedVersion=2&requestedAmount=3.5&ruleSetVersion=${encodeURIComponent(QUANTITY_RULE_SET_VERSION)}`
    ])
  })

  it('uses every Allocation operation and submits release reason only', async () => {
    const fetcher = successFetcher()
    await createTestObjectAllocation(allocationRequest(), { ...context, fetcher })
    await releaseTestObjectAllocation('allocation-1', 'completed', { ...context, fetcher })
    await getTestObjectAllocation('allocation-1', { ...context, fetcher })
    await getAllocationStatus('allocation-1', 1, { ...context, fetcher })

    expect(paths(fetcher)).toEqual([
      '/api/v1/test-object-allocations',
      '/api/v1/test-object-allocations/allocation-1/release',
      '/api/v1/test-object-allocations/allocation-1',
      `/api/v1/test-object-allocations/allocation-1/status?expectedVersion=1&ruleSetVersion=${encodeURIComponent(ALLOCATION_RULE_SET_VERSION)}`
    ])
    expect(JSON.parse(String(fetcher.mock.calls[1]?.[1]?.body))).toEqual({ reason: 'completed' })
  })

  it('uses all six Batch operations without deriving a latest version', async () => {
    const fetcher = successFetcher()
    await createBatch({
      ruleSetVersion: BATCH_RULE_SET_VERSION,
      objectScope: { legalEntityId: 'legal', laboratoryId: 'lab' },
      batchType: 'ANALYTICAL'
    }, { ...context, fetcher })
    await addBatchMember('batch-1', {
      expectedCurrentVersion: 1, ruleSetVersion: BATCH_RULE_SET_VERSION, memberType: 'SPECIMEN',
      customerId: 'customer', serviceOrderId: 'order', productCategory: 'TOYS',
      allocationId: 'allocation-1', expectedSubjectAllocationVersion: 1
    }, { ...context, fetcher })
    await addBatchEvidence('batch-1', {
      expectedCurrentVersion: 2, ruleSetVersion: BATCH_RULE_SET_VERSION, sourceSystem: 'CDS',
      externalRef: { id: 'sequence-1', version: 1 }, sha256: 'a'.repeat(64)
    }, { ...context, fetcher })
    await freezeBatch('batch-1', {
      expectedCurrentVersion: 3, ruleSetVersion: BATCH_RULE_SET_VERSION, cause: 'QC_FAILURE'
    }, { ...context, fetcher })
    await getBatch('batch-1', { ...context, fetcher })
    await getBatchStatus('batch-1', 4, { ...context, fetcher })

    expect(paths(fetcher)).toEqual([
      '/api/v1/batches', '/api/v1/batches/batch-1/members',
      '/api/v1/batches/batch-1/evidence', '/api/v1/batches/batch-1/freeze',
      '/api/v1/batches/batch-1',
      `/api/v1/batches/batch-1/status?expectedVersion=4&ruleSetVersion=${encodeURIComponent(BATCH_RULE_SET_VERSION)}`
    ])
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

function scopeRequest(): SubmitScopeMatrixVersionRequest {
  const ref = { id: 'ref-1', version: 1 }
  return {
    expectedCurrentVersion: 0,
    ruleSetVersion: SCOPE_RULE_SET_VERSION,
    objectScope: {
      legalEntityId: 'legal', laboratoryId: 'lab', customerId: 'customer',
      serviceOrderId: 'order', productCategory: 'TOYS'
    },
    lines: [{
      subjectType: 'FEATURE_NODE', subject: ref, targetMarket: ref, requirementClause: ref,
      testItem: ref, method: ref, methodOption: 'A', sampleRequirement: ref,
      evaluationMode: 'EVALUATED', workCenter: ref, reportPosition: '1'
    }]
  }
}

function quantityRequest(): CreateQuantityAccountRequest {
  return {
    ruleSetVersion: QUANTITY_RULE_SET_VERSION,
    objectScope: {
      legalEntityId: 'legal', laboratoryId: 'lab', customerId: 'customer',
      serviceOrderId: 'order', productCategory: 'TOYS'
    },
    subject: { subjectType: 'RECEIVED_ITEM', id: 'item-1', version: 1 },
    subjectQuantifiable: true, dimension: 'MASS', unit: 'GRAM',
    precisionScale: 2, conservationTolerance: 0.2
  }
}

function allocationRequest(): CreateTestObjectAllocationRequest {
  return {
    expectedCurrentVersion: 0, ruleSetVersion: ALLOCATION_RULE_SET_VERSION,
    objectScope: {
      legalEntityId: 'legal', laboratoryId: 'lab', customerId: 'customer',
      serviceOrderId: 'order', productCategory: 'TOYS'
    },
    subject: { subjectType: 'RECEIVED_ITEM', id: 'item-1', version: 1 },
    identityAssignment: { id: 'identity-1', version: 1 },
    receivedItemId: 'item-1', expectedReceivedItemVersion: 1,
    scopeMatrixId: 'scope-1', expectedScopeMatrixVersion: 1, scopeLineId: 'line-1',
    planStep: { id: 'plan-1', version: 1 }, purpose: 'test', sequenceOrder: 1,
    destructive: false, quantityAccountId: 'quantity-1', expectedQuantityAccountVersion: 1,
    requestedAmount: 1, dimension: 'MASS', unit: 'GRAM',
    storageCondition: { id: 'storage-1', version: 1 }, validUntil: '2026-08-02T08:00:00Z'
  }
}

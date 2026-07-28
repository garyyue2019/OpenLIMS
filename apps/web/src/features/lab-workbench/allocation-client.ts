import { labRequest, type LabClientContext } from './lab-api'

export const ALLOCATION_RULE_SET_VERSION = 'TASK-ALLOCATION@1.0.0'

export interface AllocationObjectContext {
  legalEntityId: string
  laboratoryId: string
  customerId: string
  serviceOrderId: string
  productCategory: string
}

export interface AllocationVersionedReference { id: string; version: number }

export interface AllocationSubjectReference extends AllocationVersionedReference {
  subjectType: 'RECEIVED_ITEM' | 'TEST_SPECIMEN' | 'TEST_PORTION'
}

export interface CreateTestObjectAllocationRequest {
  expectedCurrentVersion: number
  ruleSetVersion: typeof ALLOCATION_RULE_SET_VERSION
  objectScope: AllocationObjectContext
  subject: AllocationSubjectReference
  identityAssignment: AllocationVersionedReference
  receivedItemId: string
  expectedReceivedItemVersion: number
  scopeMatrixId: string
  expectedScopeMatrixVersion: number
  scopeLineId: string
  planStep: AllocationVersionedReference
  purpose: string
  sequenceOrder: number
  destructive: boolean
  quantityAccountId: string
  expectedQuantityAccountVersion: number
  requestedAmount: number
  dimension: string
  unit: string
  storageCondition: AllocationVersionedReference
  validUntil: string
  reservationEntryId?: string
}

export interface AllocationGateResult {
  source: 'RECEIVING' | 'SCOPE' | 'QUANTITY'
  decision: 'ALLOWED' | 'BLOCKED' | 'UNKNOWN'
  pinnedVersion?: number
  ruleSetVersion: string
  reasonCodes: string[]
}

export interface TestObjectAllocationResult {
  allocationId: string
  state: 'ACTIVE' | 'RELEASED'
  subjectAllocationVersion: number
  ruleSetVersion: string
  objectScope: AllocationObjectContext
  subject: AllocationSubjectReference
  identityAssignment: AllocationVersionedReference
  scopeMatrixId: string
  scopeLineId: string
  planStep: AllocationVersionedReference
  purpose: string
  sequenceOrder: number
  destructive: boolean
  quantityAccountId: string
  requestedAmount: number
  dimension: string
  unit: string
  storageCondition: AllocationVersionedReference
  validUntil: string
  reservationEntryId?: string
  receivingGate: AllocationGateResult
  scopeGate: AllocationGateResult
  quantityGate: AllocationGateResult
  assignedBy: string
  assignedAt: string
  releaseReason?: string
  releasedBy?: string
  releasedAt?: string
}

export interface AllocationReleaseResult {
  allocationId: string
  state: 'RELEASED'
  reason: string
  releasedBy: string
  releasedAt: string
}

export interface AllocationStatusResult {
  decision: 'ALLOWED' | 'BLOCKED' | 'UNKNOWN'
  reasonCodes: string[]
  allocationId?: string
  state?: 'ACTIVE' | 'RELEASED'
  currentSubjectAllocationVersion?: number
  ruleSetVersion: string
}

export function createTestObjectAllocation(
  request: CreateTestObjectAllocationRequest,
  context: LabClientContext
): Promise<TestObjectAllocationResult> {
  return labRequest('/api/v1/test-object-allocations', { ...context, method: 'POST', body: request })
}

export function releaseTestObjectAllocation(
  allocationId: string,
  reason: string,
  context: LabClientContext
): Promise<AllocationReleaseResult> {
  return labRequest(`/api/v1/test-object-allocations/${encodeURIComponent(allocationId)}/release`, {
    ...context, method: 'POST', body: { reason }
  })
}

export function getTestObjectAllocation(
  allocationId: string,
  context: LabClientContext
): Promise<TestObjectAllocationResult> {
  return labRequest(`/api/v1/test-object-allocations/${encodeURIComponent(allocationId)}`, context)
}

export function getAllocationStatus(
  allocationId: string,
  expectedVersion: number,
  context: LabClientContext
): Promise<AllocationStatusResult> {
  const query = new URLSearchParams({
    expectedVersion: String(expectedVersion),
    ruleSetVersion: ALLOCATION_RULE_SET_VERSION
  })
  return labRequest(
    `/api/v1/test-object-allocations/${encodeURIComponent(allocationId)}/status?${query}`,
    context
  )
}

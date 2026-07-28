import { labRequest, type LabClientContext } from './lab-api'

export const BATCH_RULE_SET_VERSION = 'BATCH-EXECUTION@1.0.0'

export interface BatchObjectContext { legalEntityId: string; laboratoryId: string }
export interface BatchVersionedReference { id: string; version: number }

export interface CreateBatchRequest {
  ruleSetVersion: typeof BATCH_RULE_SET_VERSION
  objectScope: BatchObjectContext
  batchType: 'PREPARATION' | 'PRECONDITIONING' | 'ANALYTICAL' | 'INSTRUMENT_RUN'
}

export interface AddBatchMemberRequest {
  expectedCurrentVersion: number
  ruleSetVersion: typeof BATCH_RULE_SET_VERSION
  memberType: 'SPECIMEN' | 'QC_SAMPLE'
  customerId: string
  serviceOrderId: string
  productCategory: string
  allocationId?: string
  expectedSubjectAllocationVersion?: number
  qcRef?: BatchVersionedReference
}

export interface AddBatchEvidenceRequest {
  expectedCurrentVersion: number
  ruleSetVersion: typeof BATCH_RULE_SET_VERSION
  sourceSystem: 'CDS' | 'ELN' | 'INSTRUMENT'
  externalRef: BatchVersionedReference
  sha256: string
}

export interface FreezeBatchRequest {
  expectedCurrentVersion: number
  ruleSetVersion: typeof BATCH_RULE_SET_VERSION
  cause: 'QC_FAILURE' | 'ENVIRONMENT_OUT_OF_TOLERANCE' | 'CALIBRATION_INVALID'
  approvedFollowUpRef?: BatchVersionedReference
}

export interface BatchMemberResult {
  memberId: string
  batchId: string
  batchVersion: number
  memberType: AddBatchMemberRequest['memberType']
  allocationId?: string
  subjectAllocationVersion?: number
  allocationGateDecision?: string
  allocationGateRuleSetVersion?: string
  qcRef?: BatchVersionedReference
  customerId: string
  serviceOrderId: string
  productCategory: string
  addedBy: string
  addedAt: string
}

export interface BatchEvidenceResult {
  evidenceId: string
  batchId: string
  batchVersion: number
  sourceSystem: AddBatchEvidenceRequest['sourceSystem']
  externalRef: BatchVersionedReference
  sha256: string
  recordedBy: string
  recordedAt: string
}

export interface BatchFreezeResult {
  freezeId: string
  batchId: string
  batchVersion: number
  cause: FreezeBatchRequest['cause']
  affectedMemberCount: number
  approvedFollowUpRef?: BatchVersionedReference
  frozenBy: string
  frozenAt: string
}

export interface BatchResult extends CreateBatchRequest {
  batchId: string
  state: 'ACTIVE' | 'FROZEN'
  version: number
  members: BatchMemberResult[]
  evidence: BatchEvidenceResult[]
  freeze?: BatchFreezeResult
  createdBy: string
  createdAt: string
}

export interface BatchStatusResult {
  decision: 'ALLOWED' | 'BLOCKED' | 'UNKNOWN'
  reasonCodes: string[]
  batchId?: string
  state?: 'ACTIVE' | 'FROZEN'
  currentBatchVersion?: number
  ruleSetVersion: string
}

export function createBatch(request: CreateBatchRequest, context: LabClientContext): Promise<BatchResult> {
  return labRequest('/api/v1/batches', { ...context, method: 'POST', body: request })
}

export function addBatchMember(
  batchId: string,
  request: AddBatchMemberRequest,
  context: LabClientContext
): Promise<BatchMemberResult> {
  return postBatchChild(batchId, 'members', request, context)
}

export function addBatchEvidence(
  batchId: string,
  request: AddBatchEvidenceRequest,
  context: LabClientContext
): Promise<BatchEvidenceResult> {
  return postBatchChild(batchId, 'evidence', request, context)
}

export function freezeBatch(
  batchId: string,
  request: FreezeBatchRequest,
  context: LabClientContext
): Promise<BatchFreezeResult> {
  return postBatchChild(batchId, 'freeze', request, context)
}

export function getBatch(batchId: string, context: LabClientContext): Promise<BatchResult> {
  return labRequest(`/api/v1/batches/${encodeURIComponent(batchId)}`, context)
}

export function getBatchStatus(
  batchId: string,
  expectedVersion: number,
  context: LabClientContext
): Promise<BatchStatusResult> {
  const query = new URLSearchParams({
    expectedVersion: String(expectedVersion),
    ruleSetVersion: BATCH_RULE_SET_VERSION
  })
  return labRequest(`/api/v1/batches/${encodeURIComponent(batchId)}/status?${query}`, context)
}

function postBatchChild<T>(
  batchId: string,
  action: 'members' | 'evidence' | 'freeze',
  body: unknown,
  context: LabClientContext
): Promise<T> {
  return labRequest(`/api/v1/batches/${encodeURIComponent(batchId)}/${action}`, {
    ...context, method: 'POST', body
  })
}

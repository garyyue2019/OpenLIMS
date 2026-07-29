import { labRequest, type LabClientContext } from './lab-api'

export const RESULT_RULE_SET_VERSION = 'RESULT-ADOPTION@1.0.0'

export interface ResultVersionedReference { id: string; version: number }
export interface ResultObjectContext {
  legalEntityId: string
  laboratoryId: string
  customerId: string
  serviceOrderId: string
  productCategory: string
}
export interface ResultEvidence {
  sourceSystem: 'CDS' | 'ELN' | 'INSTRUMENT' | 'MANUAL'
  externalRef: ResultVersionedReference
  sha256: string
  parserVersion: string
}
export interface CreateResultGroupRequest {
  ruleSetVersion: typeof RESULT_RULE_SET_VERSION
  objectScope: ResultObjectContext
  batchId: string
  expectedBatchVersion: number
  memberId: string
  testItem: ResultVersionedReference
  scopeLineId: string
}
export interface AddResultObservationRequest {
  expectedCurrentVersion: number
  ruleSetVersion: typeof RESULT_RULE_SET_VERSION
  kind: 'INITIAL' | 'DUPLICATE' | 'RETEST' | 'SUPPLEMENT' | 'RE_PREPARATION' | 'RE_SAMPLING'
  value: string
  unit: string
  evidence: ResultEvidence
  triggerReason?: string
  approvalRef?: ResultVersionedReference
}
export interface ResultDerivationInput { targetId: string; included: boolean; rationale?: string }
export interface AddResultDerivationRequest {
  expectedCurrentVersion: number
  ruleSetVersion: typeof RESULT_RULE_SET_VERSION
  aggregationRule: ResultVersionedReference
  value: string
  unit: string
  inputs: ResultDerivationInput[]
}
export interface RecordAdoptionRuleRequest {
  expectedCurrentVersion: number
  ruleSetVersion: typeof RESULT_RULE_SET_VERSION
  strategy: 'RETEST_REPLACES_ORIGINAL' | 'TECHNICAL_REVIEW_SELECTS'
  ruleRef: ResultVersionedReference
}
export interface AdoptResultRequest {
  expectedCurrentVersion: number
  ruleSetVersion: typeof RESULT_RULE_SET_VERSION
  targetId: string
  reviewApprovalRef?: ResultVersionedReference
}
export interface ResultObservationResult extends AddResultObservationRequest {
  observationId: string
  resultGroupId: string
  groupVersion: number
  recordedBy: string
  recordedAt: string
}
export interface ResultDerivationResult extends AddResultDerivationRequest {
  derivationId: string
  resultGroupId: string
  groupVersion: number
  recordedBy: string
  recordedAt: string
}
export interface AdoptionRuleResult {
  resultGroupId: string
  groupVersion: number
  ruleVersion: number
  strategy: RecordAdoptionRuleRequest['strategy']
  ruleRef: ResultVersionedReference
  recordedBy: string
  recordedAt: string
}
export interface ResultAdoptionResult {
  resultGroupId: string
  groupVersion: number
  adoptionVersion: number
  targetId: string
  ruleVersion: number
  reviewApprovalRef?: ResultVersionedReference
  adoptedBy: string
  adoptedAt: string
}
export interface ResultGroupResult extends CreateResultGroupRequest {
  resultGroupId: string
  version: number
  batchVersion: number
  batchGateDecision: string
  batchGateRuleSetVersion: string
  observations: ResultObservationResult[]
  derivations: ResultDerivationResult[]
  adoptionRules: AdoptionRuleResult[]
  adoptions: ResultAdoptionResult[]
  createdBy: string
  createdAt: string
}
export interface ResultAdoptionStatusResult {
  decision: 'ALLOWED' | 'BLOCKED' | 'UNKNOWN'
  reasonCodes: string[]
  resultGroupId?: string
  currentGroupVersion?: number
  effectiveTargetId?: string
  effectiveAdoptionVersion?: number
  ruleSetVersion: string
}

export function createResultGroup(
  request: CreateResultGroupRequest,
  context: LabClientContext
): Promise<ResultGroupResult> {
  return labRequest('/api/v1/result-groups', { ...context, method: 'POST', body: request })
}

export function addResultObservation(
  resultGroupId: string,
  request: AddResultObservationRequest,
  context: LabClientContext
): Promise<ResultObservationResult> {
  return postResult(resultGroupId, 'observations', request, context)
}

export function addResultDerivation(
  resultGroupId: string,
  request: AddResultDerivationRequest,
  context: LabClientContext
): Promise<ResultDerivationResult> {
  return postResult(resultGroupId, 'derivations', request, context)
}

export function recordAdoptionRule(
  resultGroupId: string,
  request: RecordAdoptionRuleRequest,
  context: LabClientContext
): Promise<AdoptionRuleResult> {
  return postResult(resultGroupId, 'adoption-rule', request, context)
}

export function adoptResult(
  resultGroupId: string,
  request: AdoptResultRequest,
  context: LabClientContext
): Promise<ResultAdoptionResult> {
  return postResult(resultGroupId, 'adoptions', request, context)
}

export function getResultGroup(
  resultGroupId: string,
  context: LabClientContext
): Promise<ResultGroupResult> {
  return labRequest(`/api/v1/result-groups/${encodeURIComponent(resultGroupId)}`, context)
}

export function getResultAdoptionStatus(
  resultGroupId: string,
  expectedVersion: number,
  context: LabClientContext
): Promise<ResultAdoptionStatusResult> {
  const query = new URLSearchParams({
    expectedVersion: String(expectedVersion),
    ruleSetVersion: RESULT_RULE_SET_VERSION
  })
  return labRequest(
    `/api/v1/result-groups/${encodeURIComponent(resultGroupId)}/adoption-status?${query}`,
    context
  )
}

function postResult<T>(
  resultGroupId: string,
  action: 'observations' | 'derivations' | 'adoption-rule' | 'adoptions',
  body: unknown,
  context: LabClientContext
): Promise<T> {
  return labRequest(`/api/v1/result-groups/${encodeURIComponent(resultGroupId)}/${action}`, {
    ...context, method: 'POST', body
  })
}

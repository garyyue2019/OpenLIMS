import { labRequest, type LabClientContext } from './lab-api'

export const SCOPE_RULE_SET_VERSION = 'SCOPE-LINE-GATE@1.0.0'

export interface ScopeObjectContext {
  legalEntityId: string
  laboratoryId: string
  customerId: string
  serviceOrderId: string
  productCategory: string
}

export interface ScopeVersionedReference { id: string; version: number }

export interface ScopeLineInput {
  subjectType: 'SUBMISSION_ITEM' | 'PRODUCT_VARIANT' | 'FEATURE_NODE'
  subject: ScopeVersionedReference
  targetMarket: ScopeVersionedReference
  requirementClause: ScopeVersionedReference
  testItem: ScopeVersionedReference
  method: ScopeVersionedReference
  methodOption: string
  sampleRequirement: ScopeVersionedReference
  evaluationMode: 'MEASURED_ONLY' | 'EVALUATED' | 'NOT_EVALUATED' | 'WAIVED'
  workCenter: ScopeVersionedReference
  reportPosition: string
  limitRule?: ScopeVersionedReference
  decisionRule?: ScopeVersionedReference
  nonEvaluationReason?: string
  waiverApproval?: ScopeVersionedReference
}

export interface SubmitScopeMatrixVersionRequest {
  expectedCurrentVersion: number
  ruleSetVersion: typeof SCOPE_RULE_SET_VERSION
  objectScope: ScopeObjectContext
  lines: ScopeLineInput[]
}

export interface ScopeLineResult extends ScopeLineInput { scopeLineId: string }

export interface ScopeMatrixVersionResult {
  scopeMatrixId: string
  version: number
  state: 'APPROVED'
  ruleSetVersion: string
  objectScope: ScopeObjectContext
  lines: ScopeLineResult[]
  approvedBy: string
  approvedAt: string
}

export interface ScopeProductionEligibilityResult {
  decision: 'ALLOWED' | 'BLOCKED' | 'UNKNOWN'
  reasonCodes: string[]
  scopeMatrixId?: string
  currentMatrixVersion?: number
  ruleSetVersion: string
}

export function createScopeMatrix(
  request: SubmitScopeMatrixVersionRequest,
  context: LabClientContext
): Promise<ScopeMatrixVersionResult> {
  return labRequest('/api/v1/scope-matrices', { ...context, method: 'POST', body: request })
}

export function reviseScopeMatrix(
  scopeMatrixId: string,
  request: SubmitScopeMatrixVersionRequest,
  context: LabClientContext
): Promise<ScopeMatrixVersionResult> {
  return labRequest(`/api/v1/scope-matrices/${encodeURIComponent(scopeMatrixId)}/versions`, {
    ...context, method: 'POST', body: request
  })
}

export function getScopeMatrixVersion(
  scopeMatrixId: string,
  version: number,
  context: LabClientContext
): Promise<ScopeMatrixVersionResult> {
  return labRequest(`/api/v1/scope-matrices/${encodeURIComponent(scopeMatrixId)}/versions/${version}`, context)
}

export function getScopeProductionEligibility(
  scopeMatrixId: string,
  expectedVersion: number,
  context: LabClientContext
): Promise<ScopeProductionEligibilityResult> {
  const query = new URLSearchParams({
    expectedVersion: String(expectedVersion),
    ruleSetVersion: SCOPE_RULE_SET_VERSION
  })
  return labRequest(
    `/api/v1/scope-matrices/${encodeURIComponent(scopeMatrixId)}/production-eligibility?${query}`,
    context
  )
}

import { labRequest, type LabClientContext } from '../lab-workbench/lab-api'

export const TOY_PRODUCT_RULE_SET_VERSION = 'TOY-AGE-GRADE@1.0.0'

export interface ToyVersionedReference { id: string; version: number }
export interface ToyObjectScope { legalEntityId: string; laboratoryId: string }

export interface RecordToyAgeDeclarationRequest {
  ruleSetVersion: typeof TOY_PRODUCT_RULE_SET_VERSION
  objectScope: ToyObjectScope
  expectedCurrentVersion: number
  declaredMinimumAgeMonths: number
  intendedUse: string
  declarationSource: string
}

export interface RecordToyAgeGradeDecisionRequest {
  ruleSetVersion: typeof TOY_PRODUCT_RULE_SET_VERSION
  objectScope: ToyObjectScope
  expectedCurrentVersion: number
  minimumAgeMonths: number
  rationale: string
  standardRef: ToyVersionedReference
  approvedBy: string
}

export interface FreezeToyAgeGradeDecisionRequest {
  ruleSetVersion: typeof TOY_PRODUCT_RULE_SET_VERSION
  expectedCurrentVersion: number
}

export interface RecordToyAccessibilityAssessmentRequest {
  ruleSetVersion: typeof TOY_PRODUCT_RULE_SET_VERSION
  objectScope: ToyObjectScope
  expectedCurrentVersion: number
  stage: 'INITIAL' | 'AFTER_NORMAL_USE' | 'AFTER_ABUSE'
  abuseEventRef?: string
  accessibleParts: string[]
}

export interface ResolveToyReassessmentTriggerRequest {
  ruleSetVersion: typeof TOY_PRODUCT_RULE_SET_VERSION
  expectedCurrentVersion: number
  resolutionRef: ToyVersionedReference
}

export interface ToyProductOverview {
  productId: string
  version: number
  ruleSetVersion: string
  objectScope: ToyObjectScope
  effectiveDecision?: Record<string, unknown>
  declarations: Array<Record<string, unknown>>
  decisions: Array<Record<string, unknown>>
  assessments: Array<Record<string, unknown>>
  triggers: Array<Record<string, unknown>>
  accessibilityStatus: 'SETTLED' | 'REASSESSMENT_PENDING'
}

export function recordToyAgeDeclaration(
  productId: string,
  request: RecordToyAgeDeclarationRequest,
  context: LabClientContext
): Promise<ToyProductOverview> {
  return postProduct(productId, 'age-declarations', request, context)
}

export function recordToyAgeGradeDecision(
  productId: string,
  request: RecordToyAgeGradeDecisionRequest,
  context: LabClientContext
): Promise<ToyProductOverview> {
  return postProduct(productId, 'age-grade-decisions', request, context)
}

export function freezeToyAgeGradeDecision(
  productId: string,
  versionNumber: number,
  request: FreezeToyAgeGradeDecisionRequest,
  context: LabClientContext
): Promise<ToyProductOverview> {
  return labRequest(
    `/api/v1/toy/products/${encodeURIComponent(productId)}/age-grade-decisions/${versionNumber}/freeze`,
    { ...context, method: 'POST', body: request }
  )
}

export function recordToyAccessibilityAssessment(
  productId: string,
  request: RecordToyAccessibilityAssessmentRequest,
  context: LabClientContext
): Promise<ToyProductOverview> {
  return postProduct(productId, 'accessibility-assessments', request, context)
}

export function resolveToyReassessmentTrigger(
  productId: string,
  triggerId: string,
  request: ResolveToyReassessmentTriggerRequest,
  context: LabClientContext
): Promise<ToyProductOverview> {
  return labRequest(
    `/api/v1/toy/products/${encodeURIComponent(productId)}/reassessment-triggers/${encodeURIComponent(triggerId)}/resolution`,
    { ...context, method: 'POST', body: request }
  )
}

export function getToyProductOverview(
  productId: string,
  context: LabClientContext
): Promise<ToyProductOverview> {
  return labRequest(`/api/v1/toy/products/${encodeURIComponent(productId)}/overview`, context)
}

function postProduct<T>(
  productId: string,
  action: 'age-declarations' | 'age-grade-decisions' | 'accessibility-assessments',
  body: unknown,
  context: LabClientContext
): Promise<T> {
  return labRequest(`/api/v1/toy/products/${encodeURIComponent(productId)}/${action}`, {
    ...context, method: 'POST', body
  })
}

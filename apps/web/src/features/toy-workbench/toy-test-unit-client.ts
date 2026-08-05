import { labRequest, type LabClientContext } from '../lab-workbench/lab-api'
import type { ToyObjectScope, ToyVersionedReference } from './toy-product-client'

export const TOY_TEST_UNIT_RULE_SET_VERSION = 'TOY-TEST-UNIT-SAMPLE-DEMAND@1.0.0'

export interface ToySequenceStepInput {
  stepId: string
  sequenceOrder: number
  taskRef: ToyVersionedReference
  destructive: boolean
  exclusiveDestructiveGroupId?: string
  shareRuleRef?: ToyVersionedReference
}

export interface ToyTestUnitInput {
  testUnitId: string
  physicalObjectRef: ToyVersionedReference
  hazardDomainRefs: ToyVersionedReference[]
  parallelNumber: number
  sequenceSteps: ToySequenceStepInput[]
}

export interface ToySampleDemandInput {
  componentId: string
  kind: 'BASE' | 'PARALLEL' | 'EXCLUSIVE_DESTRUCTIVE' | 'CHEMICAL_MINIMUM' | 'RETEST_RESERVE' | 'RETENTION'
  hazardDomainRef?: ToyVersionedReference
  testUnitId?: string
  amount: number
  dimension: string
  unit: string
  sourceRuleRef: ToyVersionedReference
  applicability: 'ALLOWED' | 'BLOCKED' | 'UNKNOWN'
}

export interface CreateToyTestUnitPlanRequest {
  ruleSetVersion: typeof TOY_TEST_UNIT_RULE_SET_VERSION
  objectScope: ToyObjectScope
  expectedCurrentVersion: number
  productVersion: number
  ageGradeDecisionVersion: number
  accessibilityAssessmentVersion: number
  scopeMatrixId: string
  scopeMatrixVersion: number
  scopeLineRefs: ToyVersionedReference[]
  sampleRuleRefs: ToyVersionedReference[]
  testUnits: ToyTestUnitInput[]
  demandInputs: ToySampleDemandInput[]
}

export interface ApproveToySampleRequirementRequest {
  expectedCurrentVersion: number
  ruleSetVersion: typeof TOY_TEST_UNIT_RULE_SET_VERSION
  inputHash: string
  approvalComment: string
}

export interface ToyQuantityGateInput {
  quantityAccountId: string
  expectedAccountVersion: number
  ruleSetVersion: string
  amount: number
  dimension: string
  unit: string
  reservationRef: string
}

export interface ToyAllocationGateInput {
  allocationId: string
  expectedSubjectAllocationVersion: number
  ruleSetVersion: string
  testUnitId: string
  sequenceStepId: string
}

export interface RequestToyAllocationRequest {
  expectedCurrentVersion: number
  ruleSetVersion: typeof TOY_TEST_UNIT_RULE_SET_VERSION
  quantityChecks: ToyQuantityGateInput[]
  allocationChecks: ToyAllocationGateInput[]
}

export interface ToyTestUnitPlanResult {
  planId: string
  productId: string
  productVersion: number
  planVersion: number
  ruleSetVersion: string
  state: 'DRAFT' | 'APPROVED' | 'SUPERSEDED'
  inputHash: string
  objectScope: ToyObjectScope
  testUnits: Array<Record<string, unknown>>
  requirement: {
    requirementId: string
    requirementVersion: number
    decision: 'PENDING_TECHNICAL_APPROVAL' | 'APPROVED' | 'UNKNOWN' | 'SUPERSEDED'
    reasonCodes: string[]
    inputHash: string
    ruleSetVersion: string
  }
  technicalApproval?: Record<string, unknown>
  downstreamDecisions: Array<Record<string, unknown>>
  [key: string]: unknown
}

export function createToyTestUnitPlan(
  productId: string,
  request: CreateToyTestUnitPlanRequest,
  context: LabClientContext
): Promise<ToyTestUnitPlanResult> {
  return labRequest(`/api/v1/toy/products/${encodeURIComponent(productId)}/test-unit-plans`, {
    ...context, method: 'POST', body: request
  })
}

export function approveToySampleRequirement(
  productId: string,
  planVersion: number,
  request: ApproveToySampleRequirementRequest,
  context: LabClientContext
): Promise<ToyTestUnitPlanResult> {
  return postPlan(productId, planVersion, 'approval', request, context)
}

export function requestToyAllocation(
  productId: string,
  planVersion: number,
  request: RequestToyAllocationRequest,
  context: LabClientContext
): Promise<ToyTestUnitPlanResult> {
  return postPlan(productId, planVersion, 'allocations', request, context)
}

export function getToyTestUnitPlan(
  productId: string,
  planVersion: number,
  context: LabClientContext
): Promise<ToyTestUnitPlanResult> {
  return labRequest(
    `/api/v1/toy/products/${encodeURIComponent(productId)}/test-unit-plans/${planVersion}`,
    context
  )
}

function postPlan<T>(
  productId: string,
  planVersion: number,
  action: 'approval' | 'allocations',
  body: unknown,
  context: LabClientContext
): Promise<T> {
  return labRequest(
    `/api/v1/toy/products/${encodeURIComponent(productId)}/test-unit-plans/${planVersion}/${action}`,
    { ...context, method: 'POST', body }
  )
}

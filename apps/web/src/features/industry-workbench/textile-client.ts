import { labRequest, type LabClientContext } from '../lab-workbench/lab-api'

export const TEXTILE_RULE_SET_VERSION = 'TEXTILE-SAMPLE-REQUIREMENT@1.0.0'

export interface TextileVersionedReference { id: string; version: number }
export interface TextileObjectScope { legalEntityId: string; laboratoryId: string }
export type TextileDirection = 'WARP' | 'WEFT' | 'LENGTHWISE' | 'CROSSWISE'

export interface TextileDemandLine {
  style: TextileVersionedReference
  colorway: TextileVersionedReference
  component: TextileVersionedReference
  material: TextileVersionedReference
  position: string
  direction: TextileDirection
  testItem: TextileVersionedReference
  parallelCount: number
  retestReserveCount: number
  retentionReserveCount: number
  destructive: boolean
  specimenLengthMm: number
  specimenWidthMm: number
  preconditioning?: TextileVersionedReference
  exclusiveDestructiveGroupId?: string
  shareGroupId?: string
}

export interface TextileAvailableFabric {
  style: TextileVersionedReference
  colorway: TextileVersionedReference
  component: TextileVersionedReference
  position: string
  availableAreaSquareMm: number
}

export interface TextileSampleRequirementCalculation {
  ruleSetVersion: typeof TEXTILE_RULE_SET_VERSION
  demandLines: TextileDemandLine[]
  availableFabrics: TextileAvailableFabric[]
}

export interface CreateTextileSampleRequirementRequest {
  requirementId: string
  expectedCurrentVersion: number
  objectScope: TextileObjectScope
  calculation: TextileSampleRequirementCalculation
}

export interface TextileSpecimenPlan {
  style: TextileVersionedReference
  colorway: TextileVersionedReference
  component: TextileVersionedReference
  position: string
  direction: TextileDirection
  testItem: TextileVersionedReference
  requiredSpecimenCount: number
  areaSquareMm: number
  shareGroupId?: string
}

export interface TextileSufficiencyGap {
  style: TextileVersionedReference
  colorway: TextileVersionedReference
  component: TextileVersionedReference
  position: string
  requiredAreaSquareMm: number
  availableAreaSquareMm: number
  gapAreaSquareMm: number
  contributingItems: Array<{ direction: string; testItem: TextileVersionedReference }>
}

export interface TextileSampleRequirementRecord {
  requirementId: string
  version: number
  objectScope: TextileObjectScope
  calculation: TextileSampleRequirementCalculation
  result: {
    decision: 'SUFFICIENT' | 'INSUFFICIENT' | 'UNKNOWN'
    reasonCodes: string[]
    specimenPlans: TextileSpecimenPlan[]
    gaps: TextileSufficiencyGap[]
    ruleSetVersion: string
  }
  inputHash: string
  createdBy: string
  createdAt: string
}

export interface TextileCuttingPlan {
  cuttingPlanId: string
  sourceItem: TextileVersionedReference
  samplingPosition: string
  direction: TextileDirection
  lengthMm: number
  widthMm: number
  plannedCount: number
  minDistanceFromSelvedgeMm: number
  templateVersion: string
  operatorId: string
  generatedSpecimenIds: string[]
}

export interface CreateTextileCuttingPlanRequest {
  cuttingPlanId: string
  expectedCurrentVersion: number
  sampleRequirementId: string
  sampleRequirementVersion: number
  sampleRequirementInputHash: string
  ruleSetVersion: typeof TEXTILE_RULE_SET_VERSION
  plan: TextileCuttingPlan
}

export interface ApproveTextileCuttingPlanRequest {
  expectedCurrentVersion: number
  sampleRequirementInputHash: string
  ruleSetVersion: typeof TEXTILE_RULE_SET_VERSION
  approvalComment?: string
}

export interface TextileCuttingPlanResult {
  cuttingPlanId: string
  version: number
  objectScope: TextileObjectScope
  sampleRequirement: TextileSampleRequirementRecord
  plan: TextileCuttingPlan
  state: 'DRAFT' | 'APPROVED' | 'SUPERSEDED'
  inputHash: string
  ruleSetVersion: string
  createdBy: string
  createdAt: string
  approval?: {
    cuttingPlanId: string
    cuttingPlanVersion: number
    sampleRequirementId: string
    sampleRequirementVersion: number
    sampleRequirementInputHash: string
    ruleSetVersion: string
    approvedBy: string
    approvedAt: string
    approvalComment?: string
  }
}

export function calculateTextileSampleRequirement(
  request: CreateTextileSampleRequirementRequest,
  context: LabClientContext
): Promise<TextileSampleRequirementRecord> {
  return labRequest('/api/v1/textile/sample-requirements', {
    ...context, method: 'POST', body: request
  })
}

export function createTextileCuttingPlan(
  request: CreateTextileCuttingPlanRequest,
  context: LabClientContext
): Promise<TextileCuttingPlanResult> {
  return labRequest('/api/v1/textile/cutting-plans', {
    ...context, method: 'POST', body: request
  })
}

export function approveTextileCuttingPlan(
  cuttingPlanId: string,
  version: number,
  request: ApproveTextileCuttingPlanRequest,
  context: LabClientContext
): Promise<TextileCuttingPlanResult> {
  return labRequest(
    `/api/v1/textile/cutting-plans/${encodeURIComponent(cuttingPlanId)}/versions/${version}/approval`,
    { ...context, method: 'POST', body: request }
  )
}

export function getTextileCuttingPlan(
  cuttingPlanId: string,
  version: number,
  context: LabClientContext
): Promise<TextileCuttingPlanResult> {
  return labRequest(
    `/api/v1/textile/cutting-plans/${encodeURIComponent(cuttingPlanId)}/versions/${version}`,
    context
  )
}

import { labRequest, type LabClientContext } from '../lab-workbench/lab-api'
import type { ToyVersionedReference } from './toy-product-client'

export const TOY_CONCLUSION_RULE_SET_VERSION = 'TOY-CONCLUSION-COVERAGE@1.0.0'

export interface CreateToyItemConclusionRequest {
  ruleSetVersion: typeof TOY_CONCLUSION_RULE_SET_VERSION
  adoptedResultRef: string
  adoptedResultVersion: number
  requirementRef: string
  requirementVersion: number
  customStatement?: never
}

export interface ToyTestUnitEvidenceInput {
  testUnitId: string
  physicalObjectRef: string
  physicalObjectVersion: number
  hazardDomainRef: string
  hazardDomainVersion: number
  adoptedResultRef: string
  adoptedResultVersion: number
  resultProvenanceGraphRef: string
  resultProvenanceGraphVersion: number
  coverageDecisionRef?: string
  coverageDecisionVersion: number
  requirementRefs?: string[]
}

export interface ToyUncoveredScopeInput {
  scope: string
  reason: 'NOT_TESTED' | 'UNKNOWN' | 'NOT_APPLICABLE'
  detail: string
}

export interface ToyExternalReferenceInput {
  issuer: string
  reference: string
  statedScope: string
  notPartOfThisConclusion: true
}

export interface CreateToyScopeConclusionRequest {
  ruleSetVersion: typeof TOY_CONCLUSION_RULE_SET_VERSION
  productRef: string
  productVersion: number
  testUnitPlanRef: string
  testUnitPlanVersion: number
  testUnits: ToyTestUnitEvidenceInput[]
  uncoveredScopes: ToyUncoveredScopeInput[]
  externalReferences?: ToyExternalReferenceInput[]
  customStatement?: never
  isFictitiousWholeItemConclusion?: false
  reauthenticationRef: ToyVersionedReference
  signingIntent: string
  signedContentHash: string
}

export interface ToyConclusionResult {
  conclusionId: string
  conclusionLevel: 'ITEM_CONFORMITY' | 'TESTED_SCOPE_CONFORMITY'
  statement: string
  approvedBy: string
  approvedAt: string
  version: number
  signatureRef?: string
  coveredHazardDomains?: string[]
  uncoveredScopes?: ToyUncoveredScopeInput[]
  externalReferences?: ToyExternalReferenceInput[]
  contentHash?: string
}

export function createToyItemConclusion(
  request: CreateToyItemConclusionRequest,
  context: LabClientContext
): Promise<ToyConclusionResult> {
  return labRequest('/api/v1/toy/conclusions/item-conformity', {
    ...context, method: 'POST', body: request
  })
}

export function createToyScopeConclusion(
  request: CreateToyScopeConclusionRequest,
  context: LabClientContext
): Promise<ToyConclusionResult> {
  return labRequest('/api/v1/toy/conclusions/tested-scope-conformity', {
    ...context, method: 'POST', body: request
  })
}

export function getToyConclusion(
  conclusionId: string,
  context: LabClientContext
): Promise<ToyConclusionResult> {
  return labRequest(`/api/v1/toy/conclusions/${encodeURIComponent(conclusionId)}`, context)
}

export function getToyConclusionsByProduct(
  productRef: string,
  productVersion: number,
  context: LabClientContext
): Promise<ToyConclusionResult[]> {
  const query = new URLSearchParams({ productRef, productVersion: String(productVersion) })
  return labRequest(`/api/v1/toy/conclusions?${query}`, context)
}

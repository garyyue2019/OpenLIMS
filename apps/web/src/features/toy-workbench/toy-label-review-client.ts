import { labRequest, type LabClientContext } from '../lab-workbench/lab-api'
import type { ToyObjectScope, ToyVersionedReference } from './toy-product-client'

export const TOY_LABEL_REVIEW_RULE_SET_VERSION = 'TOY-LABEL-REVIEW@1.0.0'

export interface ToyLabelImageEvidenceInput {
  objectRef: { bucket: string; objectKey: string }
  hash: string
}

export interface CreateToyLabelArtifactRequest {
  objectScope: ToyObjectScope
  expectedCurrentVersion: number
  artifactType: 'PACKAGING' | 'LABEL' | 'INSTRUCTION' | 'MARKETING_AGE_CLAIM'
  language: string
  market: string
  contentHash: string
  imageEvidenceRefs: ToyLabelImageEvidenceInput[]
}

export interface AppendToyLabelArtifactVersionRequest {
  expectedCurrentVersion: number
  contentHash: string
  imageEvidenceRefs: ToyLabelImageEvidenceInput[]
}

export interface CreateToyLabelReviewRequest {
  expectedCurrentVersion: number
  artifactVersion: number
  productVersion: number
  ageGradeDecisionVersion: number
  market: string
  language: string
  reviewScopeRefs: ToyVersionedReference[]
  impactRuleRef: ToyVersionedReference
  ruleSetVersion: typeof TOY_LABEL_REVIEW_RULE_SET_VERSION
  previousReviewVersion?: number
  triggerChange?: {
    changeType: 'PRODUCT_VERSION' | 'AGE_GRADE_DECISION'
    changeRef: ToyVersionedReference
  }
}

export interface DecideToyLabelReviewRequest {
  expectedCurrentVersion: number
  decision: 'APPROVED' | 'REJECTED'
  decisionReason: string
}

export interface ToyLabelArtifactResult {
  artifactId: string
  productId: string
  artifactType: string
  language: string
  market: string
  objectScope: ToyObjectScope
  versions: Array<Record<string, unknown>>
  currentVersion?: number
}

export interface ToyLabelReviewResult {
  reviewId: string
  productId: string
  artifactId: string
  artifactType: string
  objectScope: ToyObjectScope
  versions: Array<Record<string, unknown>>
  currentVersion?: number
}

export interface ToyLabelReviewStatusResult {
  decision: 'VALID' | 'RE_REVIEW_REQUIRED' | 'REJECTED' | 'UNKNOWN'
  reasonCodes: string[]
  productId: string
  artifactId?: string
  artifactVersion?: number
  reviewId?: string
  reviewVersion?: number
  productVersion?: number
  ageGradeDecisionVersion?: number
  ruleSetVersion: string
}

export interface ToyLabelReviewStatusQuery {
  productVersion: number
  ageGradeDecisionVersion: number
  market: string
  language: string
  artifactType: CreateToyLabelArtifactRequest['artifactType']
}

export function createToyLabelArtifact(
  productId: string,
  request: CreateToyLabelArtifactRequest,
  context: LabClientContext
): Promise<ToyLabelArtifactResult> {
  return labRequest(`/api/v1/toy/products/${encodeURIComponent(productId)}/label-artifacts`, {
    ...context, method: 'POST', body: request
  })
}

export function appendToyLabelArtifactVersion(
  productId: string,
  artifactId: string,
  request: AppendToyLabelArtifactVersionRequest,
  context: LabClientContext
): Promise<ToyLabelArtifactResult> {
  return labRequest(
    `/api/v1/toy/products/${encodeURIComponent(productId)}/label-artifacts/${encodeURIComponent(artifactId)}/versions`,
    { ...context, method: 'POST', body: request }
  )
}

export function createToyLabelReview(
  productId: string,
  artifactId: string,
  request: CreateToyLabelReviewRequest,
  context: LabClientContext
): Promise<ToyLabelReviewResult> {
  return labRequest(
    `/api/v1/toy/products/${encodeURIComponent(productId)}/label-artifacts/${encodeURIComponent(artifactId)}/reviews`,
    { ...context, method: 'POST', body: request }
  )
}

export function decideToyLabelReview(
  productId: string,
  reviewId: string,
  request: DecideToyLabelReviewRequest,
  context: LabClientContext
): Promise<ToyLabelReviewResult> {
  return labRequest(
    `/api/v1/toy/products/${encodeURIComponent(productId)}/label-reviews/${encodeURIComponent(reviewId)}/decision`,
    { ...context, method: 'POST', body: request }
  )
}

export function getToyLabelReviewStatus(
  productId: string,
  query: ToyLabelReviewStatusQuery,
  context: LabClientContext
): Promise<ToyLabelReviewStatusResult> {
  const search = new URLSearchParams({
    productVersion: String(query.productVersion),
    ageGradeDecisionVersion: String(query.ageGradeDecisionVersion),
    market: query.market,
    language: query.language,
    artifactType: query.artifactType,
    ruleSetVersion: TOY_LABEL_REVIEW_RULE_SET_VERSION
  })
  return labRequest(
    `/api/v1/toy/products/${encodeURIComponent(productId)}/label-reviews/status?${search}`,
    context
  )
}

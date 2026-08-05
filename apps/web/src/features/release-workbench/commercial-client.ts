import { labRequest, type LabClientContext } from '../lab-workbench/lab-api'

export const COMMERCIAL_RULE_SET_VERSION = 'COMMERCIAL@1.0.0'

export interface CommercialVersionedReference { id: string; version: number }
export interface CommercialObjectScope {
  legalEntityId: string
  laboratoryId: string
  customerId: string
  serviceOrderId: string
  productCategory: string
}
export interface SubmitCatalogRecordRequest {
  expectedCurrentVersion: number
  kind: string
  code: string
  displayName: string
  validFrom: string
  validTo?: string
  state: string
  attributes: Record<string, string>
  references: CommercialVersionedReference[]
  objectScope: CommercialObjectScope
}
export interface CatalogRecordResult extends SubmitCatalogRecordRequest {
  recordId: string
  version: number
  ruleSetVersion: string
  recordedBy: string
  recordedAt: string
}
export interface InquiryDetails {
  customerName?: string
  productCategory?: string
  quantity?: number
  quantityUnit?: string
  testPurpose?: string
  expectedTurnaroundDays?: number
  sourceDocuments: CommercialVersionedReference[]
}
export interface CreateInquiryRequest {
  details: InquiryDetails
  objectScope: CommercialObjectScope
}
export interface ResolveInquiryGapRequest { expectedCurrentVersion: number; value: string }
export interface CapabilityReviewInput {
  expectedCurrentVersion: number
  methodCapabilityConfirmed: boolean
  accreditationConfirmed: boolean
  personnelAndEquipmentConfirmed: boolean
  sampleQuantityConfirmed: boolean
  turnaroundConfirmed: boolean
  confidentialityConfirmed: boolean
  evidence: CommercialVersionedReference[]
  notes: string
}
export interface QuoteLineInput { lineCode: string; description: string; quantity: number; unitPrice: number }
export interface SubmitQuoteVersionRequest {
  expectedInquiryVersion: number
  expectedQuoteVersion: number
  scopeMatrix: CommercialVersionedReference
  currency: CommercialVersionedReference
  contractReference: CommercialVersionedReference
  promisedTurnaroundDays: number
  exclusions: string[]
  lines: QuoteLineInput[]
}
export interface RecordChangeImpactRequest {
  expectedInquiryVersion: number
  changeKind: string
  reason: string
}
export interface InquiryResult {
  inquiryId: string
  inquiryNumber: string
  version: number
  ruleSetVersion: string
  state: string
  details: InquiryDetails
  objectScope: CommercialObjectScope
  gaps: unknown[]
  capabilityReviews: unknown[]
  quoteVersions: unknown[]
  changeImpacts: unknown[]
  recordedBy: string
  recordedAt: string
}

export function createCatalogRecord(
  request: SubmitCatalogRecordRequest,
  context: LabClientContext
): Promise<CatalogRecordResult> {
  return labRequest('/api/v1/catalog-records', { ...context, method: 'POST', body: request })
}

export function reviseCatalogRecord(
  recordId: string,
  request: SubmitCatalogRecordRequest,
  context: LabClientContext
): Promise<CatalogRecordResult> {
  return labRequest(`/api/v1/catalog-records/${encodeURIComponent(recordId)}/versions`, {
    ...context, method: 'POST', body: request
  })
}

export function getCatalogRecord(
  recordId: string,
  version: number,
  context: LabClientContext
): Promise<CatalogRecordResult> {
  return labRequest(
    `/api/v1/catalog-records/${encodeURIComponent(recordId)}/versions/${version}`,
    context
  )
}

export function createInquiry(
  request: CreateInquiryRequest,
  context: LabClientContext
): Promise<InquiryResult> {
  return labRequest('/api/v1/inquiries', { ...context, method: 'POST', body: request })
}

export function getInquiry(inquiryId: string, context: LabClientContext): Promise<InquiryResult> {
  return labRequest(`/api/v1/inquiries/${encodeURIComponent(inquiryId)}`, context)
}

export function resolveInquiryGap(
  inquiryId: string,
  gapId: string,
  request: ResolveInquiryGapRequest,
  context: LabClientContext
): Promise<InquiryResult> {
  return labRequest(
    `/api/v1/inquiries/${encodeURIComponent(inquiryId)}/gaps/${encodeURIComponent(gapId)}/resolution`,
    { ...context, method: 'POST', body: request }
  )
}

export function recordCapabilityReview(
  inquiryId: string,
  request: CapabilityReviewInput,
  context: LabClientContext
): Promise<InquiryResult> {
  return postInquiry(inquiryId, 'capability-reviews', request, context)
}

export function createQuoteVersion(
  inquiryId: string,
  request: SubmitQuoteVersionRequest,
  context: LabClientContext
): Promise<InquiryResult> {
  return postInquiry(inquiryId, 'quote-versions', request, context)
}

export function recordCommercialChangeImpact(
  inquiryId: string,
  request: RecordChangeImpactRequest,
  context: LabClientContext
): Promise<InquiryResult> {
  return postInquiry(inquiryId, 'change-impacts', request, context)
}

function postInquiry(
  inquiryId: string,
  action: 'capability-reviews' | 'quote-versions' | 'change-impacts',
  body: unknown,
  context: LabClientContext
): Promise<InquiryResult> {
  return labRequest(`/api/v1/inquiries/${encodeURIComponent(inquiryId)}/${action}`, {
    ...context, method: 'POST', body
  })
}

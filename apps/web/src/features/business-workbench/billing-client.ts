import { labRequest, type LabClientContext } from '../lab-workbench/lab-api'

export const BILLING_RULE_SET_VERSION = 'BILLING-EVIDENCE@1.0.0'
export const BILLING_EXPORT_RULE_SET_VERSION = 'BILLING-EXPORT@1.0.0'
export const BILLING_HANDOFF_RULE_SET_VERSION = 'BILLING-HANDOFF@1.0.0'

export interface BillingVersionedReference {
  id: string
  version: number
}

export interface BillingObjectScope {
  legalEntityId: string
  laboratoryId: string
  customerId: string
  serviceOrderId: string
  productCategory: string
}

export interface CreateBillingEvidenceRequest {
  ruleSetVersion: typeof BILLING_RULE_SET_VERSION
  objectScope: BillingObjectScope
  resultGroupId: string
  expectedGroupVersion: number
  contractBaseline: BillingVersionedReference
  chargeDimension: string
  billingRuleVersion: string
  amount: number
  currency: BillingVersionedReference
  zeroAmountReason?: string
}

export interface AddBillingAdjustmentRequest {
  ruleSetVersion: typeof BILLING_RULE_SET_VERSION
  amount: number
  reason: string
}

export interface BillingAdjustmentResult {
  adjustmentId: string
  billingEvidenceId: string
  amount: number
  reason: string
  recordedBy: string
  recordedAt: string
}

export interface BillingEvidenceResult extends CreateBillingEvidenceRequest {
  billingEvidenceId: string
  stage: 'SERVICE_COMPLETED' | 'BILLABLE_CANDIDATE'
  groupVersion: number
  adoptionTargetId: string
  adjustments: BillingAdjustmentResult[]
  recordedBy: string
  recordedAt: string
}

export interface BillingEvidenceStatusResult {
  decision: 'ALLOWED' | 'BLOCKED' | 'UNKNOWN'
  reasonCodes: string[]
  billingEvidenceId?: string
  stage?: string
  amount?: number
  adjustmentCount?: number
  ruleSetVersion: string
}
export interface CreateBillingExportBatchRequest {
  ruleSetVersion: typeof BILLING_EXPORT_RULE_SET_VERSION
  billingEvidenceIds: string[]
  exportSchemaVersion: string
  idempotencyKey: string
}
export interface CreateBillingHandoffRequest {
  ruleSetVersion: typeof BILLING_HANDOFF_RULE_SET_VERSION
  externalSystem: 'ERP' | 'INVOICE'
  mode: 'AUTOMATED' | 'MANUAL'
  endpoint: BillingVersionedReference
  idempotencyKey: string
}
export interface ErpPostingConfirmation {
  voucherNumber: string
  companyCode: string
  fiscalYear: number
  period: number
  postingDate: string
}
export interface RecordBillingHandoffAttemptRequest {
  ruleSetVersion: typeof BILLING_HANDOFF_RULE_SET_VERSION
  idempotencyKey: string
  outcome: 'SUCCEEDED' | 'FAILED' | 'UNKNOWN' | 'DIFFERENT'
  externalReference?: string
  detailCode?: string
  erpPosting?: ErpPostingConfirmation
}

export function createBillingEvidence(
  request: CreateBillingEvidenceRequest,
  context: LabClientContext
): Promise<BillingEvidenceResult> {
  return labRequest('/api/v1/billing-evidence', { ...context, method: 'POST', body: request })
}

export function addBillingAdjustment(
  billingEvidenceId: string,
  request: AddBillingAdjustmentRequest,
  context: LabClientContext
): Promise<BillingAdjustmentResult> {
  return labRequest(
    `/api/v1/billing-evidence/${encodeURIComponent(billingEvidenceId)}/adjustments`,
    { ...context, method: 'POST', body: request }
  )
}

export function getBillingEvidence(
  billingEvidenceId: string,
  context: LabClientContext
): Promise<BillingEvidenceResult> {
  return labRequest(`/api/v1/billing-evidence/${encodeURIComponent(billingEvidenceId)}`, context)
}

export function getBillingEvidenceStatus(
  billingEvidenceId: string,
  context: LabClientContext
): Promise<BillingEvidenceStatusResult> {
  const query = new URLSearchParams({ ruleSetVersion: BILLING_RULE_SET_VERSION })
  return labRequest(
    `/api/v1/billing-evidence/${encodeURIComponent(billingEvidenceId)}/status?${query}`,
    context
  )
}

export function createBillingExportBatch(
  request: CreateBillingExportBatchRequest,
  context: LabClientContext
): Promise<unknown> {
  return labRequest('/api/v1/billing-export-batches', { ...context, method: 'POST', body: request })
}

export function getBillingExportBatch(batchId: string, context: LabClientContext): Promise<unknown> {
  return labRequest(`/api/v1/billing-export-batches/${encodeURIComponent(batchId)}`, context)
}

export function createBillingHandoff(
  batchId: string,
  request: CreateBillingHandoffRequest,
  context: LabClientContext
): Promise<unknown> {
  return labRequest(`/api/v1/billing-export-batches/${encodeURIComponent(batchId)}/handoffs`, {
    ...context, method: 'POST', body: request
  })
}

export function getBillingHandoff(handoffId: string, context: LabClientContext): Promise<unknown> {
  return labRequest(`/api/v1/billing-handoffs/${encodeURIComponent(handoffId)}`, context)
}

export function recordBillingHandoffAttempt(
  handoffId: string,
  request: RecordBillingHandoffAttemptRequest,
  context: LabClientContext
): Promise<unknown> {
  return labRequest(`/api/v1/billing-handoffs/${encodeURIComponent(handoffId)}/attempts`, {
    ...context, method: 'POST', body: request
  })
}

export function getBillingDifferenceQueue(
  externalSystem: 'ERP' | 'INVOICE' | undefined,
  context: LabClientContext
): Promise<unknown> {
  const query = externalSystem ? `?${new URLSearchParams({ externalSystem })}` : ''
  return labRequest(`/api/v1/billing-handoffs/differences${query}`, context)
}

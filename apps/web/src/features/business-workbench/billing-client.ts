import { labRequest, type LabClientContext } from '../lab-workbench/lab-api'

export const BILLING_RULE_SET_VERSION = 'BILLING-EVIDENCE@1.0.0'

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

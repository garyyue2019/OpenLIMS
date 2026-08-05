import { labRequest, type LabClientContext } from '../lab-workbench/lab-api'

export const AI_RUNTIME_RULE_SET_VERSION = 'AI-RUNTIME@1.0.0'
export const AI_OUTPUT_RULE_SET_VERSION = 'AI-DOC-EXTRACTION@1.0.0'

export interface AiVersionedReference { id: string; version: number }
export interface AiObjectScope {
  legalEntityId: string
  laboratoryId: string
  customerId: string
  serviceOrderId: string
  productCategory: string
}
export interface AiRunEnvelope {
  model: AiVersionedReference
  gatewayRoute: string
  promptTemplate: AiVersionedReference
  outputSchema: AiVersionedReference
  inputRefs: AiVersionedReference[]
}
export interface CreateAiRunRequest {
  ruleSetVersion: typeof AI_RUNTIME_RULE_SET_VERSION
  objectScope: AiObjectScope
  envelope: AiRunEnvelope
  validationProfile: AiVersionedReference
  allowedFields: string[]
  allowedUnits: string[]
  idempotencyKey: string
}
export interface RecordAiDispositionRequest {
  expectedRunVersion: number
  ruleSetVersion: typeof AI_RUNTIME_RULE_SET_VERSION
  candidateId: string
  kind: 'ACCEPT' | 'MODIFY' | 'SPLIT' | 'MERGE' | 'REJECT'
  reason: string
  idempotencyKey: string
  humanValue?: string
}
export interface AiRunResult {
  runId: string
  version: number
  status: 'PENDING' | 'ACCEPTED' | 'QUARANTINED' | 'PROVIDER_DISABLED' | 'PROVIDER_FAILED'
  objectScope: AiObjectScope
  envelope: AiRunEnvelope
  providerStatus: string
  providerExternalReference?: string
  providerFailureCode?: string
  originalOutput?: unknown
  validation?: unknown
  dispositions: unknown[]
  humanReviewRequired: boolean
  manualFallbackRequired: boolean
  ruleSetVersion: string
}
export interface AiReviewQueueResult { runs: AiRunResult[]; ruleSetVersion: string }

export function createAiRun(
  request: CreateAiRunRequest,
  context: LabClientContext
): Promise<AiRunResult> {
  return labRequest('/api/v1/ai-runs', { ...context, method: 'POST', body: request })
}

export function getAiRun(runId: string, context: LabClientContext): Promise<AiRunResult> {
  return labRequest(`/api/v1/ai-runs/${encodeURIComponent(runId)}`, context)
}

export function recordAiDisposition(
  runId: string,
  request: RecordAiDispositionRequest,
  context: LabClientContext
): Promise<unknown> {
  return labRequest(`/api/v1/ai-runs/${encodeURIComponent(runId)}/dispositions`, {
    ...context, method: 'POST', body: request
  })
}

export function getAiReviewQueue(
  status: 'ACCEPTED' | 'QUARANTINED' | undefined,
  context: LabClientContext
): Promise<AiReviewQueueResult> {
  const query = status ? `?${new URLSearchParams({ status })}` : ''
  return labRequest(`/api/v1/ai-review-queue${query}`, context)
}

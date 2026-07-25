export const exceptionMatrixVersion = 'OD-005@1.0.0'
export type ReceivingExceptionType = 'QUANTITY_SHORTAGE' | 'TEMPERATURE_EXCURSION' | 'DAMAGED' | 'CONTAMINATION' | 'LABEL_CONFLICT' | 'IDENTITY_MISMATCH' | 'IDENTITY_INDETERMINATE'
export type ReceivingExceptionDecisionType = 'AWAIT_CUSTOMER' | 'CONDITIONAL_ACCEPT' | 'REJECT' | 'SAFETY_HOLD'

export interface CreateReceivingExceptionRequest {
  receivedItemId: string
  expectedItemVersion: number
  type: ReceivingExceptionType
  observedAt: string
  description: string
  evidenceRefs: string[]
  evidenceHashes: string[]
}

export interface SubmitReceivingExceptionDecisionRequest {
  expectedVersion: number
  decisionType: ReceivingExceptionDecisionType
  allowedActions: string[]
  prohibitedActions: string[]
  validUntil?: string
  evidenceRefs: string[]
  evidenceHashes: string[]
  technicalImpact: string
  rationale: string
  matrixVersion: string
}

export interface ReceivingExceptionDecision {
  decisionId: string
  version: number
  decisionType: ReceivingExceptionDecisionType
  allowedActions: string[]
  prohibitedActions: string[]
  validUntil?: string
  rationale: string
  decidedAt: string
  decidedBy: string
}

export interface ReceivingExceptionResult {
  exceptionId: string
  receivedItemId: string
  receivedItemNumber: string
  itemVersion: number
  itemState: 'QUARANTINED'
  type: ReceivingExceptionType
  severity: 'STANDARD' | 'SAFETY_CRITICAL'
  description: string
  observedAt: string
  evidenceRefs: string[]
  evidenceHashes: string[]
  createdBy: string
  createdAt: string
  status: 'OPEN' | 'AWAITING_CUSTOMER' | 'CONDITIONALLY_ACCEPTED' | 'REJECTED' | 'SAFETY_HOLD'
  version: number
  decisions: ReceivingExceptionDecision[]
}

export class ReceivingExceptionApiError extends Error {
  constructor(public readonly errorCode: string, public readonly status: number) {
    super(errorCode)
    this.name = 'ReceivingExceptionApiError'
  }
}

export function createReceivingException(payload: CreateReceivingExceptionRequest, token: string, fetcher: typeof fetch = fetch): Promise<ReceivingExceptionResult> {
  return request('/api/v1/exceptions', token, payload, fetcher)
}

export function getReceivingException(exceptionId: string, token: string, fetcher: typeof fetch = fetch): Promise<ReceivingExceptionResult> {
  return request(`/api/v1/exceptions/${encodeURIComponent(exceptionId)}`, token, undefined, fetcher)
}

export function submitReceivingExceptionDecision(exceptionId: string, payload: SubmitReceivingExceptionDecisionRequest, token: string, fetcher: typeof fetch = fetch): Promise<ReceivingExceptionResult> {
  return request(`/api/v1/exceptions/${encodeURIComponent(exceptionId)}/decisions`, token, payload, fetcher)
}

async function request(
  path: string,
  token: string,
  payload: CreateReceivingExceptionRequest | SubmitReceivingExceptionDecisionRequest | undefined,
  fetcher: typeof fetch
): Promise<ReceivingExceptionResult> {
  const response = await fetcher(path, {
    method: payload ? 'POST' : 'GET',
    headers: { Authorization: `Bearer ${token}`, ...(payload ? { 'Content-Type': 'application/json' } : {}) },
    ...(payload ? { body: JSON.stringify(payload) } : {})
  })
  const content = await response.json() as Record<string, unknown>
  if (!response.ok) {
    throw new ReceivingExceptionApiError(
      typeof content.errorCode === 'string' ? content.errorCode : 'EXCEPTION_UNEXPECTED_RESPONSE',
      response.status
    )
  }
  return content as unknown as ReceivingExceptionResult
}

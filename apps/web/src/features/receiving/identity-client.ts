export const identityRuleSetVersion = 'REC-ELIGIBILITY@1.0.0'

export type IdentityAssessmentState = 'NOT_STARTED' | 'IN_PROGRESS' | 'MATCHED' | 'MISMATCHED' | 'INDETERMINATE'
export type IdentityDecisionOutcome = 'MATCHED' | 'MISMATCHED' | 'INDETERMINATE'

export interface IdentityDeclarationSnapshot {
  receivedItemId: string
  snapshotVersion: number
  itemVersion: number
  declaredDescription: string
  model: string
  batch: string
  serialNumber?: string
  color: string
  capturedAt: string
}

export interface IdentityObservation {
  observationId: string
  version: number
  expectedItemVersion: number
  observedLabels: string[]
  observedModel: string
  observedBatch: string
  appearance: string
  attachmentRefs: string[]
  attachmentHashes: string[]
  observedAt: string
  observedBy: string
}

export interface IdentityDecision {
  decisionId: string
  version: number
  observationVersion: number
  declarationSnapshotVersion: number
  outcome: IdentityDecisionOutcome
  reasonCode: string
  rationale: string
  ruleSetVersion: string
  decidedAt: string
  decidedBy: string
}

export interface IdentityAssessmentResult {
  receivedItemId: string
  receivedItemNumber: string
  currentState: 'QUARANTINED'
  itemVersion: number
  assessmentState: IdentityAssessmentState
  assessmentVersion: number
  declarationSnapshot?: IdentityDeclarationSnapshot
  observations: IdentityObservation[]
  decisions: IdentityDecision[]
}

export interface CreateIdentityObservationRequest {
  expectedItemVersion: number
  observedLabels: string[]
  observedModel: string
  observedBatch: string
  appearance: string
  attachmentRefs: string[]
  attachmentHashes: string[]
}

export interface SubmitIdentityDecisionRequest {
  expectedItemVersion: number
  observationVersion: number
  declarationSnapshotVersion: number
  outcome: IdentityDecisionOutcome
  reasonCode: string
  rationale: string
  ruleSetVersion: string
}

export class IdentityApiError extends Error {
  constructor(
    public readonly errorCode: string,
    public readonly status: number
  ) {
    super(errorCode)
    this.name = 'IdentityApiError'
  }
}

export function getIdentityAssessment(
  receivedItemId: string,
  accessToken: string,
  fetcher: typeof fetch = fetch
): Promise<IdentityAssessmentResult> {
  return request(
    `/api/v1/received-items/${encodeURIComponent(receivedItemId)}/identity-assessment`,
    accessToken,
    undefined,
    fetcher
  )
}

export function createIdentityObservation(
  receivedItemId: string,
  payload: CreateIdentityObservationRequest,
  accessToken: string,
  fetcher: typeof fetch = fetch
): Promise<IdentityAssessmentResult> {
  return request(
    `/api/v1/received-items/${encodeURIComponent(receivedItemId)}/identity-observations`,
    accessToken,
    payload,
    fetcher
  )
}

export function submitIdentityDecision(
  receivedItemId: string,
  payload: SubmitIdentityDecisionRequest,
  accessToken: string,
  fetcher: typeof fetch = fetch
): Promise<IdentityAssessmentResult> {
  return request(
    `/api/v1/received-items/${encodeURIComponent(receivedItemId)}/identity-decisions`,
    accessToken,
    payload,
    fetcher
  )
}

async function request(
  path: string,
  accessToken: string,
  payload: CreateIdentityObservationRequest | SubmitIdentityDecisionRequest | undefined,
  fetcher: typeof fetch
): Promise<IdentityAssessmentResult> {
  const response = await fetcher(path, {
    method: payload ? 'POST' : 'GET',
    headers: {
      Authorization: `Bearer ${accessToken}`,
      ...(payload ? { 'Content-Type': 'application/json' } : {})
    },
    ...(payload ? { body: JSON.stringify(payload) } : {})
  })
  const content = await response.json() as Record<string, unknown>
  if (!response.ok) {
    throw new IdentityApiError(
      typeof content.errorCode === 'string' ? content.errorCode : 'IDENTITY_UNEXPECTED_RESPONSE',
      response.status
    )
  }
  return content as unknown as IdentityAssessmentResult
}

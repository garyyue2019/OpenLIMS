export const releaseRuleSetVersion = 'REC-RELEASE@2.0.0'

export type ReceivingReleaseOutcome = 'RELEASED' | 'RELEASED_WITH_CONSTRAINTS'
export type ReceivingReleaseState = 'ACCEPTED' | 'CONDITIONALLY_ACCEPTED'

export interface SubmitReceivingReleaseDecisionRequest {
  expectedItemVersion: number
  ruleSetVersion: string
  rationale: string
}

export interface ReceivingReleaseExceptionReference {
  exceptionId: string
  status: 'CONDITIONALLY_ACCEPTED'
  exceptionVersion: number
  decisionId: string
  decisionVersion: number
  matrixVersion: string
}

export interface ReceivingReleaseDecisionResult {
  releaseDecisionId: string
  version: number
  receivedItemId: string
  receivedItemNumber: string
  boundItemVersion: number
  itemVersion: number
  state: ReceivingReleaseState
  identityDecisionId: string
  identityDecisionVersion: number
  exceptionDecisionVersions: ReceivingReleaseExceptionReference[]
  releaseRuleVersion: string
  exceptionMatrixVersion: string
  outcome: ReceivingReleaseOutcome
  allowedActions: string[]
  prohibitedActions: string[]
  constraintsValidUntil?: string
  rationale: string
  approvedAt: string
  approvedBy: string
}

export class ReceivingReleaseApiError extends Error {
  constructor(public readonly errorCode: string, public readonly status: number) {
    super(errorCode)
    this.name = 'ReceivingReleaseApiError'
  }
}

export async function submitReceivingReleaseDecision(
  receivedItemId: string,
  payload: SubmitReceivingReleaseDecisionRequest,
  token: string,
  fetcher: typeof fetch = fetch
): Promise<ReceivingReleaseDecisionResult> {
  const response = await fetcher(
    `/api/v1/received-items/${encodeURIComponent(receivedItemId)}/release-decisions`,
    {
      method: 'POST',
      headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' },
      body: JSON.stringify(payload)
    }
  )
  const content = await response.json() as Record<string, unknown>
  if (!response.ok) {
    throw new ReceivingReleaseApiError(
      typeof content.errorCode === 'string' ? content.errorCode : 'RELEASE_UNEXPECTED_RESPONSE',
      response.status
    )
  }
  return content as unknown as ReceivingReleaseDecisionResult
}

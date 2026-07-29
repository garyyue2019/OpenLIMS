import { labRequest, type LabClientContext } from './lab-api'

export const QC_RULE_SET_VERSION = 'QC-IMPACT@1.0.0'

export interface QcVersionedReference { id: string; version: number }
export interface QcObjectContext { legalEntityId: string; laboratoryId: string }
export interface CreateQcRunRequest {
  ruleSetVersion: typeof QC_RULE_SET_VERSION
  objectScope: QcObjectContext
  batchId: string
  expectedBatchVersion: number
  method: QcVersionedReference
  qcRuleSet: QcVersionedReference
}
export interface AddQcResultRequest {
  expectedCurrentVersion: number
  ruleSetVersion: typeof QC_RULE_SET_VERSION
  rule: QcVersionedReference
  controlType: 'BLANK' | 'SPIKE' | 'DUPLICATE' | 'REFERENCE_MATERIAL' | 'CALIBRATION_CHECK'
  observedValue: string
  verdict: 'PASS' | 'FAIL'
  verdictBasis: string
}
export interface RecordQcVerdictRequest {
  expectedCurrentVersion: number
  ruleSetVersion: typeof QC_RULE_SET_VERSION
}
export interface QcImpactTarget {
  targetType: 'RESULT_GROUP' | 'TASK'
  targetId: string
  targetVersion: number
}
export interface RecordQcImpactRequest extends RecordQcVerdictRequest { targets: QcImpactTarget[] }
export interface RecordQcDeviationApprovalRequest extends RecordQcVerdictRequest {
  approvalRef: QcVersionedReference
  reason: string
}
export interface SatisfyQcReleaseGateRequest extends RecordQcVerdictRequest {
  kind: 'INVESTIGATION' | 'IMPACT_SCOPE' | 'VALIDITY_DECISION' | 'ADOPTION_RULE' | 'TECHNICAL_REVIEW'
  evidenceRef: QcVersionedReference
}
export type ReleaseQcBlockRequest = RecordQcVerdictRequest
export interface QcResultEntry extends AddQcResultRequest {
  qcResultId: string
  qcRunId: string
  recordedBy: string
  recordedAt: string
}
export interface QcImpactEntry extends QcImpactTarget {
  impactId: string
  qcRunId: string
  recordedBy: string
  recordedAt: string
}
export interface QcReleaseGateEntry {
  gateId: string
  qcRunId: string
  kind: SatisfyQcReleaseGateRequest['kind']
  evidenceRef: QcVersionedReference
  satisfiedBy: string
  satisfiedAt: string
}
export interface QcDeviationApprovalEntry {
  deviationId: string
  qcRunId: string
  approvalRef: QcVersionedReference
  reason: string
  approvedBy: string
  approvedAt: string
}
export interface QcRunResult extends CreateQcRunRequest {
  qcRunId: string
  version: number
  state: 'OPEN' | 'PASSED' | 'FAILED' | 'RELEASED'
  batchVersion: number
  batchGateDecision: string
  batchGateRuleSetVersion: string
  results: QcResultEntry[]
  impact: QcImpactEntry[]
  gates: QcReleaseGateEntry[]
  deviationApprovals: QcDeviationApprovalEntry[]
  releasedBy?: string
  releasedAt?: string
  openedBy: string
  openedAt: string
}
export interface QcReportabilityResult {
  decision: 'ALLOWED' | 'BLOCKED' | 'UNKNOWN'
  reasonCodes: string[]
  qcRunId: string
  targetId: string
  currentVersion?: number
  outstandingGates: string[]
  ruleSetVersion: string
}

export function openQcRun(request: CreateQcRunRequest, context: LabClientContext): Promise<QcRunResult> {
  return labRequest('/api/v1/qc-runs', { ...context, method: 'POST', body: request })
}

export function recordQcResult(
  qcRunId: string, request: AddQcResultRequest, context: LabClientContext
): Promise<QcRunResult> {
  return postQc(qcRunId, 'results', request, context)
}

export function recordQcVerdict(
  qcRunId: string, request: RecordQcVerdictRequest, context: LabClientContext
): Promise<QcRunResult> {
  return postQc(qcRunId, 'verdict', request, context)
}

export function recordQcImpact(
  qcRunId: string, request: RecordQcImpactRequest, context: LabClientContext
): Promise<QcRunResult> {
  return postQc(qcRunId, 'impact', request, context)
}

export function recordQcDeviationApproval(
  qcRunId: string, request: RecordQcDeviationApprovalRequest, context: LabClientContext
): Promise<QcRunResult> {
  return postQc(qcRunId, 'deviation-approval', request, context)
}

export function satisfyQcReleaseGate(
  qcRunId: string, request: SatisfyQcReleaseGateRequest, context: LabClientContext
): Promise<QcRunResult> {
  return postQc(qcRunId, 'gates', request, context)
}

export function releaseQcBlock(
  qcRunId: string, request: ReleaseQcBlockRequest, context: LabClientContext
): Promise<QcRunResult> {
  return postQc(qcRunId, 'release', request, context)
}

export function getQcRun(qcRunId: string, context: LabClientContext): Promise<QcRunResult> {
  return labRequest(`/api/v1/qc-runs/${encodeURIComponent(qcRunId)}`, context)
}

export function getQcReportability(
  qcRunId: string,
  expectedRunVersion: number,
  targetId: string,
  context: LabClientContext
): Promise<QcReportabilityResult> {
  const query = new URLSearchParams({
    expectedRunVersion: String(expectedRunVersion),
    ruleSetVersion: QC_RULE_SET_VERSION,
    targetId
  })
  return labRequest(`/api/v1/qc-runs/${encodeURIComponent(qcRunId)}/reportability?${query}`, context)
}

function postQc<T>(
  qcRunId: string,
  action: 'results' | 'verdict' | 'impact' | 'deviation-approval' | 'gates' | 'release',
  body: unknown,
  context: LabClientContext
): Promise<T> {
  return labRequest(`/api/v1/qc-runs/${encodeURIComponent(qcRunId)}/${action}`, {
    ...context, method: 'POST', body
  })
}

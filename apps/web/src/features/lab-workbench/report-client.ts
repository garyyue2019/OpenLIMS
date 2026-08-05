import { labRequest, type LabClientContext } from './lab-api'

export const REPORT_RULE_SET_VERSION = 'RPT-ISSUANCE@1.0.0'
export const REPORT_DELIVERY_RULE_SET_VERSION = 'RPT-DELIVERY@1.0.0'

export interface ReportVersionedReference { id: string; version: number }
export interface ReportObjectContext {
  legalEntityId: string
  laboratoryId: string
  customerId: string
  serviceOrderId: string
  productCategory: string
}
export interface AccreditationScopeReference extends ReportVersionedReference { sha256: string }
export interface AccreditationClaim {
  siteId: string
  method: ReportVersionedReference
  productMatrix: string
  parameterRange: string
  validUntil: string
  signatoryId: string
}
export interface ReportTraceReferences {
  batchId: string
  allocationId: string
  receivedItemId: string
  requirementSnapshot: ReportVersionedReference
}
export interface CreateReportRequest {
  ruleSetVersion: typeof REPORT_RULE_SET_VERSION
  objectScope: ReportObjectContext
  reportNumber: string
}
export interface AddReportLineRequest {
  expectedCurrentVersion: number
  ruleSetVersion: typeof REPORT_RULE_SET_VERSION
  lineNumber: number
  resultGroupId: string
  expectedGroupVersion: number
  scopeLineId: string
  scopePartition: 'ACTUAL_TESTED' | 'APPROVED_COVERAGE' | 'NOT_EVALUATED' | 'CUSTOMER_DECLARED' | 'LABORATORY_CONCLUSION'
  traceRefs: ReportTraceReferences
  accreditationRef: AccreditationScopeReference
  accreditationClaim: AccreditationClaim
  qcRuns: ReportVersionedReference[]
  instrumentFileId: string
  expectedInstrumentFileVersion: number
  expectedReceivedItemVersion: number
  scopeMatrixId: string
  expectedScopeMatrixVersion: number
  expectedAllocationVersion: number
  expectedBatchVersion: number
  subcontractingDisclosure?: ReportVersionedReference
  claimsAccreditation?: boolean
}
export interface EvaluateReportGateRequest {
  expectedCurrentVersion: number
  ruleSetVersion: typeof REPORT_RULE_SET_VERSION
  signatoryId: string
}
export interface SubmitReportForApprovalRequest {
  expectedCurrentVersion: number
  ruleSetVersion: typeof REPORT_RULE_SET_VERSION
}
export interface ReportBlocker {
  objectRef: string
  objectType: string
  source: string
  ruleSetVersion: string
  reasonCode: string
  allowedNextSteps: string[]
  lineNumber?: number
}
export interface ReportLineAccreditationVerdict {
  lineNumber: number
  status: 'ACCREDITED' | 'NOT_ACCREDITED' | 'UNKNOWN'
  failedDimensions: string[]
}
export interface ReportLineGateReferences {
  qcRuns: ReportVersionedReference[]
  instrumentFileId: string
  instrumentFileVersion: number
  scopeMatrixId: string
  scopeMatrixVersion: number
  receivedItemVersion: number
  allocationVersion: number
  batchVersion: number
}
export interface ReportLineResult {
  lineId: string
  reportId: string
  lineNumber: number
  resultGroupId: string
  groupVersion: number
  adoptionTargetId: string
  adoptionRuleSetVersion: string
  scopeLineId: string
  scopePartition: AddReportLineRequest['scopePartition']
  traceRefs: ReportTraceReferences
  gateRefs: ReportLineGateReferences
  accreditationRef: AccreditationScopeReference
  accreditationClaim: AccreditationClaim
  claimsAccreditation: boolean
  subcontractingDisclosure?: ReportVersionedReference
  addedBy: string
  addedAt: string
}
export interface ReportGateEvaluationResult {
  evaluationId: string
  reportId: string
  reportVersion: number
  decision: 'ALLOWED' | 'BLOCKED' | 'UNKNOWN'
  blockers: ReportBlocker[]
  accreditationVerdicts: ReportLineAccreditationVerdict[]
  signatoryId: string
  evaluatedBy: string
  evaluatedAt: string
}
export interface ReportResult extends CreateReportRequest {
  reportId: string
  version: number
  state: 'DRAFT' | 'PENDING_APPROVAL'
  lines: ReportLineResult[]
  gateEvaluations: ReportGateEvaluationResult[]
  createdBy: string
  createdAt: string
}
export interface ReportIssuanceGateResult {
  decision: 'ALLOWED' | 'BLOCKED' | 'UNKNOWN'
  reasonCodes: string[]
  reportId: string
  currentVersion?: number
  blockers: ReportBlocker[]
  accreditationVerdicts: ReportLineAccreditationVerdict[]
  ruleSetVersion: string
}
export interface PendingContentHashResult {
  reportId: string
  nextVersionNumber: number
  contentHash: string
  canonicalContent: string
  lineCount: number
  ruleSetVersion: string
}
export interface IssueReportRequest {
  expectedCurrentVersion: number
  ruleSetVersion: typeof REPORT_RULE_SET_VERSION
  reauthenticationRef: ReportVersionedReference
  signingIntent: string
  expectedContentHash: string
  signatoryId: string
}
export interface PerformControlledActionRequest {
  expectedCurrentVersion: number
  ruleSetVersion: typeof REPORT_RULE_SET_VERSION
  versionNumber: number
  kind: 'CORRECTION' | 'SUPPLEMENT' | 'WITHDRAWAL' | 'VOID' | 'SUPERSESSION'
  reason: string
  impactAssessmentRef?: ReportVersionedReference
  supersedingReportNumber?: string
}
export interface ReportVersionSnapshotResult {
  snapshotId: string
  reportId: string
  versionNumber: number
  contentHash: string
  canonicalContent: string
  lineCount: number
  createdBy: string
  createdAt: string
}
export interface ReportSignatureResult {
  signatureId: string
  reportId: string
  versionNumber: number
  contentHash: string
  reauthenticationRef: ReportVersionedReference
  signingIntent: string
  signatoryId: string
  signedAt: string
}
export interface ReportControlledActionResult {
  actionId: string
  reportId: string
  versionNumber: number
  kind: PerformControlledActionRequest['kind']
  impactAssessmentRef?: ReportVersionedReference
  supersedingReportNumber?: string
  reason: string
  performedBy: string
  performedAt: string
}
export interface ReportVersionEntry {
  versionNumber: number
  state: 'ISSUED' | 'SUPERSEDED' | 'WITHDRAWN' | 'VOIDED'
  contentHash: string
  signedAt: string
  supersededBy?: number
}
export interface ReportVerificationResult {
  reportId: string
  reportNumber: string
  currentVersionNumber?: number
  chainState: 'ACTIVE' | 'VOIDED'
  versions: ReportVersionEntry[]
  supersedingReportNumber?: string
  ruleSetVersion: string
}
export interface ReportVersionDetailResult {
  reportId: string
  versionNumber: number
  state: ReportVersionEntry['state']
  snapshot: ReportVersionSnapshotResult
  signature: ReportSignatureResult
  actions: ReportControlledActionResult[]
  ruleSetVersion: string
}
export interface CreateReportDeliveryRequest {
  ruleSetVersion: typeof REPORT_DELIVERY_RULE_SET_VERSION
  recipientId: string
  channel: 'PORTAL' | 'EMAIL' | 'API' | 'MANUAL'
  destinationHash: string
  idempotencyKey: string
}
export interface CreateReportDownloadGrantRequest {
  ruleSetVersion: typeof REPORT_DELIVERY_RULE_SET_VERSION
  recipientId: string
  expiresAt: string
}
export interface QueueReportNotificationRequest {
  ruleSetVersion: typeof REPORT_DELIVERY_RULE_SET_VERSION
  channel: 'PORTAL' | 'EMAIL' | 'API' | 'MANUAL'
  destinationHash: string
  payload: ReportVersionedReference
  idempotencyKey: string
}
export interface RecordReportNotificationAttemptRequest {
  ruleSetVersion: typeof REPORT_DELIVERY_RULE_SET_VERSION
  idempotencyKey: string
  outcome: 'DELIVERED' | 'FAILED' | 'UNKNOWN'
  externalReference?: string
  detailCode?: string
}

export function createReport(request: CreateReportRequest, context: LabClientContext): Promise<ReportResult> {
  return labRequest('/api/v1/reports', { ...context, method: 'POST', body: request })
}
export function addReportLine(
  reportId: string, request: AddReportLineRequest, context: LabClientContext
): Promise<ReportResult> { return postReport(reportId, 'lines', request, context) }
export function evaluateReportGate(
  reportId: string, request: EvaluateReportGateRequest, context: LabClientContext
): Promise<ReportResult> { return postReport(reportId, 'gate-evaluation', request, context) }
export function submitReportForApproval(
  reportId: string, request: SubmitReportForApprovalRequest, context: LabClientContext
): Promise<ReportResult> { return postReport(reportId, 'submit-for-approval', request, context) }
export function getReport(reportId: string, context: LabClientContext): Promise<ReportResult> {
  return labRequest(`/api/v1/reports/${encodeURIComponent(reportId)}`, context)
}
export function getReportIssuanceGate(
  reportId: string, expectedReportVersion: number, context: LabClientContext
): Promise<ReportIssuanceGateResult> {
  const query = new URLSearchParams({
    expectedReportVersion: String(expectedReportVersion),
    ruleSetVersion: REPORT_RULE_SET_VERSION
  })
  return labRequest(`/api/v1/reports/${encodeURIComponent(reportId)}/issuance-gate?${query}`, context)
}
export function getReportPendingContentHash(
  reportId: string, context: LabClientContext
): Promise<PendingContentHashResult> {
  return labRequest(`/api/v1/reports/${encodeURIComponent(reportId)}/pending-content-hash`, context)
}
export function issueReport(
  reportId: string, request: IssueReportRequest, context: LabClientContext
): Promise<ReportSignatureResult> { return postReport(reportId, 'issuance', request, context) }
export function performReportControlledAction(
  reportId: string, request: PerformControlledActionRequest, context: LabClientContext
): Promise<ReportControlledActionResult> { return postReport(reportId, 'controlled-actions', request, context) }
export function getReportVerification(
  reportId: string, context: LabClientContext
): Promise<ReportVerificationResult> {
  return labRequest(`/api/v1/reports/${encodeURIComponent(reportId)}/verification`, context)
}
export function getReportVersion(
  reportId: string, versionNumber: number, context: LabClientContext
): Promise<ReportVersionDetailResult> {
  return labRequest(
    `/api/v1/reports/${encodeURIComponent(reportId)}/versions/${versionNumber}`,
    context
  )
}

export function createReportDelivery(
  reportId: string,
  versionNumber: number,
  request: CreateReportDeliveryRequest,
  context: LabClientContext
): Promise<unknown> {
  return labRequest(
    `/api/v1/reports/${encodeURIComponent(reportId)}/versions/${versionNumber}/deliveries`,
    { ...context, method: 'POST', body: request }
  )
}

export function getReportDelivery(deliveryId: string, context: LabClientContext): Promise<unknown> {
  return labRequest(`/api/v1/report-deliveries/${encodeURIComponent(deliveryId)}`, context)
}

export function createReportDownloadGrant(
  deliveryId: string,
  request: CreateReportDownloadGrantRequest,
  context: LabClientContext
): Promise<unknown> {
  return labRequest(`/api/v1/report-deliveries/${encodeURIComponent(deliveryId)}/download-grants`, {
    ...context, method: 'POST', body: request
  })
}

export function downloadReportVersion(accessToken: string, context: LabClientContext): Promise<unknown> {
  return labRequest(`/api/v1/report-downloads/${encodeURIComponent(accessToken)}`, context)
}

export function queueReportNotification(
  deliveryId: string,
  request: QueueReportNotificationRequest,
  context: LabClientContext
): Promise<unknown> {
  return labRequest(`/api/v1/report-deliveries/${encodeURIComponent(deliveryId)}/notifications`, {
    ...context, method: 'POST', body: request
  })
}

export function recordReportNotificationAttempt(
  notificationId: string,
  request: RecordReportNotificationAttemptRequest,
  context: LabClientContext
): Promise<unknown> {
  return labRequest(`/api/v1/report-notifications/${encodeURIComponent(notificationId)}/attempts`, {
    ...context, method: 'POST', body: request
  })
}

function postReport<T>(
  reportId: string,
  action: 'lines' | 'gate-evaluation' | 'submit-for-approval' | 'issuance' | 'controlled-actions',
  body: unknown,
  context: LabClientContext
): Promise<T> {
  return labRequest(`/api/v1/reports/${encodeURIComponent(reportId)}/${action}`, {
    ...context, method: 'POST', body
  })
}

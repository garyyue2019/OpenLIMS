import { describe, expect, it, vi } from 'vitest'
import {
  getInstrumentFile,
  getInstrumentImportStatus,
  INSTRUMENT_RULE_SET_VERSION,
  registerInstrumentFile,
  resolveInstrumentImportException,
  submitInstrumentRows
} from './instrument-client'
import {
  addResultDerivation,
  addResultObservation,
  adoptResult,
  createResultGroup,
  executeResultCalculation,
  getResultAccreditationEligibility,
  getResultAdoptionStatus,
  getResultGroup,
  recordResultAccreditationAssessment,
  recordAdoptionRule,
  RESULT_ACCREDITATION_RULE_SET_VERSION,
  RESULT_CALCULATION_RULE_SET_VERSION,
  RESULT_RULE_SET_VERSION,
  type ExecuteResultCalculationRequest,
  type RecordResultAccreditationAssessmentRequest
} from './result-client'
import {
  getQcReportability,
  getQcRun,
  openQcRun,
  QC_RULE_SET_VERSION,
  recordQcDeviationApproval,
  recordQcImpact,
  recordQcResult,
  recordQcVerdict,
  releaseQcBlock,
  satisfyQcReleaseGate
} from './qc-client'
import {
  addReportLine,
  createReport,
  createReportDelivery,
  createReportDownloadGrant,
  downloadReportVersion,
  evaluateReportGate,
  getReport,
  getReportDelivery,
  getReportIssuanceGate,
  getReportPendingContentHash,
  getReportVerification,
  getReportVersion,
  issueReport,
  performReportControlledAction,
  queueReportNotification,
  recordReportNotificationAttempt,
  REPORT_DELIVERY_RULE_SET_VERSION,
  REPORT_RULE_SET_VERSION,
  submitReportForApproval,
  type AddReportLineRequest,
  type CreateReportDeliveryRequest,
  type CreateReportDownloadGrantRequest,
  type QueueReportNotificationRequest,
  type RecordReportNotificationAttemptRequest
} from './report-client'

const context = { accessToken: 'token', correlationId: 'corr' }
const versionedRef = { id: 'ref-1', version: 1 }

describe('second-flow typed clients', () => {
  it('covers every Instrument endpoint with exact status query and encoded ids', async () => {
    const fetcher = successFetcher()
    await registerInstrumentFile({
      ruleSetVersion: INSTRUMENT_RULE_SET_VERSION,
      objectScope: { legalEntityId: 'legal', laboratoryId: 'lab' },
      externalRef: versionedRef, sha256: 'a'.repeat(64), sourceSystem: 'INSTRUMENT',
      instrumentRef: versionedRef, parserVersion: 'parser@1.0.0', declaredRowCount: 1
    }, { ...context, fetcher })
    await submitInstrumentRows('file/1', {
      expectedCurrentVersion: 1, ruleSetVersion: INSTRUMENT_RULE_SET_VERSION,
      rows: [{ rowNumber: 1, sampleNumber: 'S1', batchPosition: 'A1', parameter: 'P', unit: 'U', rawValue: '1', parsedValue: '1' }]
    }, { ...context, fetcher })
    await resolveInstrumentImportException('file/1', 'exception/1', {
      expectedCurrentVersion: 2, ruleSetVersion: INSTRUMENT_RULE_SET_VERSION,
      kind: 'REJECT_ROW', reason: 'invalid row'
    }, { ...context, fetcher })
    await getInstrumentFile('file/1', { ...context, fetcher })
    await getInstrumentImportStatus('file/1', 3, { ...context, fetcher })

    expect(paths(fetcher)).toEqual([
      '/api/v1/instrument-files',
      '/api/v1/instrument-files/file%2F1/rows',
      '/api/v1/instrument-files/file%2F1/exceptions/exception%2F1/resolution',
      '/api/v1/instrument-files/file%2F1',
      `/api/v1/instrument-files/file%2F1/import-status?expectedFileVersion=3&ruleSetVersion=${encodeURIComponent(INSTRUMENT_RULE_SET_VERSION)}`
    ])
  })

  it('covers every Result endpoint without client-selected trusted context', async () => {
    const fetcher = successFetcher()
    await createResultGroup({
      ruleSetVersion: RESULT_RULE_SET_VERSION,
      objectScope: {
        legalEntityId: 'legal', laboratoryId: 'lab', customerId: 'customer',
        serviceOrderId: 'order', productCategory: 'TOYS'
      },
      batchId: 'batch', expectedBatchVersion: 1, memberId: 'member',
      testItem: versionedRef, scopeLineId: 'line'
    }, { ...context, fetcher })
    await addResultObservation('result-1', {
      expectedCurrentVersion: 1, ruleSetVersion: RESULT_RULE_SET_VERSION,
      kind: 'INITIAL', value: '1', unit: 'mg/kg',
      evidence: { sourceSystem: 'INSTRUMENT', externalRef: versionedRef, sha256: 'b'.repeat(64), parserVersion: 'parser@1.0.0' }
    }, { ...context, fetcher })
    await addResultDerivation('result-1', {
      expectedCurrentVersion: 2, ruleSetVersion: RESULT_RULE_SET_VERSION,
      aggregationRule: versionedRef, value: '1', unit: 'mg/kg',
      inputs: [{ targetId: 'observation-1', included: true }]
    }, { ...context, fetcher })
    await recordAdoptionRule('result-1', {
      expectedCurrentVersion: 3, ruleSetVersion: RESULT_RULE_SET_VERSION,
      strategy: 'RETEST_REPLACES_ORIGINAL', ruleRef: versionedRef
    }, { ...context, fetcher })
    await adoptResult('result-1', {
      expectedCurrentVersion: 4, ruleSetVersion: RESULT_RULE_SET_VERSION, targetId: 'observation-1'
    }, { ...context, fetcher })
    const calculation: ExecuteResultCalculationRequest = {
      expectedCurrentVersion: 5, ruleSetVersion: RESULT_CALCULATION_RULE_SET_VERSION,
      inputs: [{ targetId: 'observation-1', coefficient: 1 }],
      rule: {
        calculationRule: versionedRef, unitConversionRule: versionedRef,
        inputUnit: 'mg/kg', outputUnit: 'mg/kg', unitMultiplier: 1, unitOffset: 0,
        dilutionFactor: 1, quantityFactor: 1, decimalPlaces: 2, roundingMode: 'HALF_UP',
        limitOperator: 'BETWEEN', limitEvaluationBasis: 'ROUNDED', lowerLimit: 0, upperLimit: 10
      }
    }
    await executeResultCalculation('result-1', calculation, { ...context, fetcher })
    const assessment: RecordResultAccreditationAssessmentRequest = {
      expectedCurrentVersion: 6, ruleSetVersion: RESULT_ACCREDITATION_RULE_SET_VERSION,
      stage: 'RESULT', targetId: 'observation-1', accreditation: versionedRef,
      method: versionedRef, siteId: 'site-1', productOrMatrix: 'toy', parameter: 'lead',
      rangeUnit: 'mg/kg', rangeLower: 0, rangeUpper: 10,
      validFrom: '2026-01-01T00:00:00Z', validTo: '2027-01-01T00:00:00Z',
      authorizedActorIds: ['analyst-1']
    }
    await recordResultAccreditationAssessment('result-1', assessment, { ...context, fetcher })
    await getResultGroup('result-1', { ...context, fetcher })
    await getResultAdoptionStatus('result-1', 5, { ...context, fetcher })
    await getResultAccreditationEligibility('result-1', 7, { ...context, fetcher })

    expect(paths(fetcher)).toEqual([
      '/api/v1/result-groups', '/api/v1/result-groups/result-1/observations',
      '/api/v1/result-groups/result-1/derivations', '/api/v1/result-groups/result-1/adoption-rule',
      '/api/v1/result-groups/result-1/adoptions', '/api/v1/result-groups/result-1/calculations',
      '/api/v1/result-groups/result-1/accreditation-assessments', '/api/v1/result-groups/result-1',
      `/api/v1/result-groups/result-1/adoption-status?expectedVersion=5&ruleSetVersion=${encodeURIComponent(RESULT_RULE_SET_VERSION)}`,
      `/api/v1/result-groups/result-1/accreditation-eligibility?expectedVersion=7&ruleSetVersion=${encodeURIComponent(RESULT_ACCREDITATION_RULE_SET_VERSION)}`
    ])
    expect(JSON.parse(requestBodies(fetcher)[5]!)).toEqual(calculation)
    expect(JSON.parse(requestBodies(fetcher)[6]!)).toEqual(assessment)
    expect(requestBodies(fetcher).join('')).not.toContain('organizationGroupId')
    expect(requestBodies(fetcher).join('')).not.toContain('actorId')
  })

  it('covers all QC writes, detail, and target-pinned reportability', async () => {
    const fetcher = successFetcher()
    await openQcRun({
      ruleSetVersion: QC_RULE_SET_VERSION,
      objectScope: { legalEntityId: 'legal', laboratoryId: 'lab' },
      batchId: 'batch', expectedBatchVersion: 1, method: versionedRef, qcRuleSet: versionedRef
    }, { ...context, fetcher })
    const current = { expectedCurrentVersion: 1, ruleSetVersion: QC_RULE_SET_VERSION } as const
    await recordQcResult('qc-1', {
      ...current, rule: versionedRef, controlType: 'BLANK', observedValue: '0',
      verdict: 'PASS', verdictBasis: 'within limit'
    }, { ...context, fetcher })
    await recordQcVerdict('qc-1', current, { ...context, fetcher })
    await recordQcImpact('qc-1', {
      ...current, targets: [{ targetType: 'RESULT_GROUP', targetId: 'result-1', targetVersion: 1 }]
    }, { ...context, fetcher })
    await recordQcDeviationApproval('qc-1', {
      ...current, approvalRef: versionedRef, reason: 'approved deviation'
    }, { ...context, fetcher })
    await satisfyQcReleaseGate('qc-1', {
      ...current, kind: 'INVESTIGATION', evidenceRef: versionedRef
    }, { ...context, fetcher })
    await releaseQcBlock('qc-1', current, { ...context, fetcher })
    await getQcRun('qc-1', { ...context, fetcher })
    await getQcReportability('qc-1', 7, 'result/1', { ...context, fetcher })

    expect(paths(fetcher)).toEqual([
      '/api/v1/qc-runs', '/api/v1/qc-runs/qc-1/results', '/api/v1/qc-runs/qc-1/verdict',
      '/api/v1/qc-runs/qc-1/impact', '/api/v1/qc-runs/qc-1/deviation-approval',
      '/api/v1/qc-runs/qc-1/gates', '/api/v1/qc-runs/qc-1/release', '/api/v1/qc-runs/qc-1',
      `/api/v1/qc-runs/qc-1/reportability?expectedRunVersion=7&ruleSetVersion=${encodeURIComponent(QC_RULE_SET_VERSION)}&targetId=result%2F1`
    ])
  })

  it('covers all Report issuance and delivery operations with exact version pins', async () => {
    const fetcher = successFetcher()
    await createReport({
      ruleSetVersion: REPORT_RULE_SET_VERSION,
      objectScope: {
        legalEntityId: 'legal', laboratoryId: 'lab', customerId: 'customer',
        serviceOrderId: 'order', productCategory: 'TOYS'
      },
      reportNumber: 'REPORT-1'
    }, { ...context, fetcher })
    await addReportLine('report/1', reportLineRequest(), { ...context, fetcher })
    const current = { expectedCurrentVersion: 2, ruleSetVersion: REPORT_RULE_SET_VERSION } as const
    await evaluateReportGate('report/1', { ...current, signatoryId: 'signatory' }, { ...context, fetcher })
    await submitReportForApproval('report/1', current, { ...context, fetcher })
    await getReport('report/1', { ...context, fetcher })
    await getReportIssuanceGate('report/1', 4, { ...context, fetcher })
    await getReportPendingContentHash('report/1', { ...context, fetcher })
    await issueReport('report/1', {
      ...current, reauthenticationRef: versionedRef, signingIntent: 'approve exact content',
      expectedContentHash: 'd'.repeat(64), signatoryId: 'signatory'
    }, { ...context, fetcher })
    await performReportControlledAction('report/1', {
      ...current, versionNumber: 1, kind: 'WITHDRAWAL', reason: 'withdraw'
    }, { ...context, fetcher })
    await getReportVerification('report/1', { ...context, fetcher })
    await getReportVersion('report/1', 1, { ...context, fetcher })
    const delivery: CreateReportDeliveryRequest = {
      ruleSetVersion: REPORT_DELIVERY_RULE_SET_VERSION, recipientId: 'recipient-1',
      channel: 'PORTAL', destinationHash: 'e'.repeat(64), idempotencyKey: 'delivery-1'
    }
    await createReportDelivery('report/1', 1, delivery, { ...context, fetcher })
    await getReportDelivery('delivery/1', { ...context, fetcher })
    const grant: CreateReportDownloadGrantRequest = {
      ruleSetVersion: REPORT_DELIVERY_RULE_SET_VERSION, recipientId: 'recipient-1',
      expiresAt: '2026-08-06T00:00:00Z'
    }
    await createReportDownloadGrant('delivery/1', grant, { ...context, fetcher })
    await downloadReportVersion('token/1', { ...context, fetcher })
    const notification: QueueReportNotificationRequest = {
      ruleSetVersion: REPORT_DELIVERY_RULE_SET_VERSION, channel: 'EMAIL',
      destinationHash: 'f'.repeat(64), payload: versionedRef, idempotencyKey: 'notification-1'
    }
    await queueReportNotification('delivery/1', notification, { ...context, fetcher })
    const attempt: RecordReportNotificationAttemptRequest = {
      ruleSetVersion: REPORT_DELIVERY_RULE_SET_VERSION, idempotencyKey: 'attempt-1',
      outcome: 'FAILED', detailCode: 'SMTP_TIMEOUT'
    }
    await recordReportNotificationAttempt('notification/1', attempt, { ...context, fetcher })

    expect(paths(fetcher)).toEqual([
      '/api/v1/reports', '/api/v1/reports/report%2F1/lines',
      '/api/v1/reports/report%2F1/gate-evaluation', '/api/v1/reports/report%2F1/submit-for-approval',
      '/api/v1/reports/report%2F1',
      `/api/v1/reports/report%2F1/issuance-gate?expectedReportVersion=4&ruleSetVersion=${encodeURIComponent(REPORT_RULE_SET_VERSION)}`,
      '/api/v1/reports/report%2F1/pending-content-hash', '/api/v1/reports/report%2F1/issuance',
      '/api/v1/reports/report%2F1/controlled-actions', '/api/v1/reports/report%2F1/verification',
      '/api/v1/reports/report%2F1/versions/1',
      '/api/v1/reports/report%2F1/versions/1/deliveries',
      '/api/v1/report-deliveries/delivery%2F1',
      '/api/v1/report-deliveries/delivery%2F1/download-grants',
      '/api/v1/report-downloads/token%2F1',
      '/api/v1/report-deliveries/delivery%2F1/notifications',
      '/api/v1/report-notifications/notification%2F1/attempts'
    ])
    expect(JSON.parse(requestBodies(fetcher)[11]!)).toEqual(delivery)
    expect(JSON.parse(requestBodies(fetcher)[13]!)).toEqual(grant)
    expect(JSON.parse(requestBodies(fetcher)[15]!)).toEqual(notification)
    expect(JSON.parse(requestBodies(fetcher)[16]!)).toEqual(attempt)
  })
})

function reportLineRequest(): AddReportLineRequest {
  return {
    expectedCurrentVersion: 1, ruleSetVersion: REPORT_RULE_SET_VERSION, lineNumber: 1,
    resultGroupId: 'result-1', expectedGroupVersion: 1, scopeLineId: 'scope-line',
    scopePartition: 'ACTUAL_TESTED' as const,
    traceRefs: {
      batchId: 'batch', allocationId: 'allocation', receivedItemId: 'item',
      requirementSnapshot: versionedRef
    },
    accreditationRef: { ...versionedRef, sha256: 'c'.repeat(64) },
    accreditationClaim: {
      siteId: 'site', method: versionedRef, productMatrix: 'matrix', parameterRange: 'range',
      validUntil: '2026-12-31T00:00:00Z', signatoryId: 'signatory'
    },
    qcRuns: [versionedRef], instrumentFileId: 'file', expectedInstrumentFileVersion: 1,
    expectedReceivedItemVersion: 1, scopeMatrixId: 'scope', expectedScopeMatrixVersion: 1,
    expectedAllocationVersion: 1, expectedBatchVersion: 1, claimsAccreditation: true
  }
}

function successFetcher() {
  return vi.fn(async () => new Response('{}', {
    status: 200, headers: { 'Content-Type': 'application/json' }
  })) as unknown as typeof fetch & { mock: { calls: [string, RequestInit][] } }
}

function paths(fetcher: ReturnType<typeof successFetcher>): string[] {
  return fetcher.mock.calls.map(call => call[0])
}

function requestBodies(fetcher: ReturnType<typeof successFetcher>): string[] {
  return fetcher.mock.calls.map(call => String(call[1]?.body ?? ''))
}

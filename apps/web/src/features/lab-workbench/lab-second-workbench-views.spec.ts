import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { LabApiError } from './lab-api'

const mocks = vi.hoisted(() => ({
  authSnapshot: {
    value: {
      status: 'authenticated',
      user: {
        access_token: 'token',
        profile: { capability: ['instrument.import', 'result.record', 'qc.manage', 'report.manage'] }
      }
    } as Record<string, unknown>
  },
  signIn: vi.fn(),
  instrument: { register: vi.fn(), rows: vi.fn(), resolve: vi.fn(), get: vi.fn(), status: vi.fn() },
  result: {
    create: vi.fn(), observation: vi.fn(), derivation: vi.fn(), calculation: vi.fn(),
    rule: vi.fn(), adopt: vi.fn(), accreditation: vi.fn(), get: vi.fn(),
    status: vi.fn(), eligibility: vi.fn()
  },
  qc: { create: vi.fn(), result: vi.fn(), verdict: vi.fn(), impact: vi.fn(), deviation: vi.fn(), gate: vi.fn(), release: vi.fn(), get: vi.fn(), status: vi.fn() },
  report: {
    create: vi.fn(), line: vi.fn(), evaluate: vi.fn(), submit: vi.fn(), get: vi.fn(),
    gate: vi.fn(), hash: vi.fn(), issue: vi.fn(), action: vi.fn(), verification: vi.fn(),
    version: vi.fn(), delivery: vi.fn(), deliveryGet: vi.fn(), grant: vi.fn(), download: vi.fn(),
    notification: vi.fn(), notificationAttempt: vi.fn()
  }
}))

vi.mock('../../auth-store', () => ({
  authSnapshot: mocks.authSnapshot,
  signIn: mocks.signIn
}))
vi.mock('vue-router', () => ({ useRoute: () => ({ fullPath: '/workbench/second-flow' }) }))
vi.mock('./instrument-client', async (importOriginal) => ({
  ...await importOriginal<typeof import('./instrument-client')>(),
  registerInstrumentFile: mocks.instrument.register,
  submitInstrumentRows: mocks.instrument.rows,
  resolveInstrumentImportException: mocks.instrument.resolve,
  getInstrumentFile: mocks.instrument.get,
  getInstrumentImportStatus: mocks.instrument.status
}))
vi.mock('./result-client', async (importOriginal) => ({
  ...await importOriginal<typeof import('./result-client')>(),
  createResultGroup: mocks.result.create,
  addResultObservation: mocks.result.observation,
  addResultDerivation: mocks.result.derivation,
  executeResultCalculation: mocks.result.calculation,
  recordAdoptionRule: mocks.result.rule,
  adoptResult: mocks.result.adopt,
  recordResultAccreditationAssessment: mocks.result.accreditation,
  getResultGroup: mocks.result.get,
  getResultAdoptionStatus: mocks.result.status,
  getResultAccreditationEligibility: mocks.result.eligibility
}))
vi.mock('./qc-client', async (importOriginal) => ({
  ...await importOriginal<typeof import('./qc-client')>(),
  openQcRun: mocks.qc.create,
  recordQcResult: mocks.qc.result,
  recordQcVerdict: mocks.qc.verdict,
  recordQcImpact: mocks.qc.impact,
  recordQcDeviationApproval: mocks.qc.deviation,
  satisfyQcReleaseGate: mocks.qc.gate,
  releaseQcBlock: mocks.qc.release,
  getQcRun: mocks.qc.get,
  getQcReportability: mocks.qc.status
}))
vi.mock('./report-client', async (importOriginal) => ({
  ...await importOriginal<typeof import('./report-client')>(),
  createReport: mocks.report.create,
  addReportLine: mocks.report.line,
  evaluateReportGate: mocks.report.evaluate,
  submitReportForApproval: mocks.report.submit,
  getReport: mocks.report.get,
  getReportIssuanceGate: mocks.report.gate,
  getReportPendingContentHash: mocks.report.hash,
  issueReport: mocks.report.issue,
  performReportControlledAction: mocks.report.action,
  getReportVerification: mocks.report.verification,
  getReportVersion: mocks.report.version,
  createReportDelivery: mocks.report.delivery,
  getReportDelivery: mocks.report.deliveryGet,
  createReportDownloadGrant: mocks.report.grant,
  downloadReportVersion: mocks.report.download,
  queueReportNotification: mocks.report.notification,
  recordReportNotificationAttempt: mocks.report.notificationAttempt
}))

import InstrumentWorkbenchView from './InstrumentWorkbenchView.vue'
import QcWorkbenchView from './QcWorkbenchView.vue'
import ReportWorkbenchView from './ReportWorkbenchView.vue'
import ResultWorkbenchView from './ResultWorkbenchView.vue'

beforeEach(() => {
  vi.clearAllMocks()
  mocks.authSnapshot.value = {
    status: 'authenticated',
    user: {
      access_token: 'token',
      profile: { capability: ['instrument.import', 'result.record', 'qc.manage', 'report.manage'] }
    }
  }
  mocks.instrument.register.mockResolvedValue(instrumentResult())
  mocks.result.adopt.mockResolvedValue({
    resultGroupId: 'result-1', groupVersion: 5, adoptionVersion: 1, targetId: 'observation-1',
    ruleVersion: 1, adoptedBy: 'actor', adoptedAt: '2026-07-29T00:00:00Z'
  })
  mocks.result.calculation.mockResolvedValue({ resultGroupId: 'result-1', groupVersion: 4 })
  mocks.result.eligibility.mockResolvedValue({
    decision: 'ALLOWED', reasonCodes: [], resultGroupId: 'result-1', currentGroupVersion: 7,
    ruleSetVersion: 'RESULT-ACCREDITATION@1.0.0'
  })
  mocks.qc.create.mockResolvedValue(qcResult())
  mocks.report.get.mockResolvedValue(reportResult())
  mocks.report.delivery.mockResolvedValue({ deliveryId: 'delivery-1', reportId: 'report-1', versionNumber: 1 })
  mocks.report.download.mockResolvedValue({ reportId: 'report-1', versionNumber: 1, content: 'fixed-version' })
})

describe('second laboratory workbench views', () => {
  it('registers an Instrument file and rejects a malformed evidence hash locally', async () => {
    const wrapper = mount(InstrumentWorkbenchView)
    await wrapper.findAll('form')[0]!.trigger('submit')
    await flushPromises()

    expect(mocks.instrument.register).toHaveBeenCalledTimes(1)
    expect(mocks.instrument.register.mock.calls[0]?.[0]).toMatchObject({
      ruleSetVersion: 'INST-IMPORT@1.0.0', declaredRowCount: 1
    })
    expect(wrapper.text()).toContain('instrument-file-1')

    const textarea = wrapper.get('textarea')
    const invalid = JSON.parse(textarea.element.value)
    invalid.sha256 = 'bad'
    await textarea.setValue(JSON.stringify(invalid))
    await wrapper.findAll('form')[0]!.trigger('submit')
    expect(mocks.instrument.register).toHaveBeenCalledTimes(1)
    expect(wrapper.text()).toContain('64 位 SHA-256')
  })

  it('adopts a Result only with an explicit group id and exact target payload', async () => {
    const wrapper = mount(ResultWorkbenchView)
    await wrapper.get('select').setValue('adopt')
    await flushPromises()
    await wrapper.findAll('form')[0]!.get('input').setValue('result-1')
    await wrapper.findAll('form')[0]!.trigger('submit')
    await flushPromises()

    expect(mocks.result.adopt).toHaveBeenCalledWith(
      'result-1',
      expect.objectContaining({
        expectedCurrentVersion: 5,
        ruleSetVersion: 'RESULT-ADOPTION@1.0.0',
        targetId: 'observation-or-derivation-id'
      }),
      { accessToken: 'token' }
    )
    expect(wrapper.html()).not.toContain('organizationGroupId')
    expect(wrapper.html()).not.toContain('actorId')
  })

  it('executes a versioned Result calculation and checks server accreditation eligibility', async () => {
    const wrapper = mount(ResultWorkbenchView)
    const operationForm = wrapper.findAll('form')[0]!
    await operationForm.get('select').setValue('calculation')
    await flushPromises()
    await operationForm.get('input').setValue('result-1')
    await operationForm.trigger('submit')
    await flushPromises()

    expect(mocks.result.calculation).toHaveBeenCalledWith(
      'result-1',
      expect.objectContaining({
        ruleSetVersion: 'RESULT-CALCULATION@1.0.0',
        rule: expect.objectContaining({ roundingMode: 'HALF_UP', limitOperator: 'BETWEEN' })
      }),
      { accessToken: 'token' }
    )

    const lookup = wrapper.findAll('form')[1]!
    await lookup.findAll('input')[0]!.setValue('result-1')
    await lookup.findAll('input')[1]!.setValue('7')
    await lookup.findAll('button').find(button => button.text() === '检查认可资格')!.trigger('click')
    await flushPromises()
    expect(mocks.result.eligibility).toHaveBeenCalledWith('result-1', 7, { accessToken: 'token' })
  })

  it('keeps QC writes disabled without capability and explains the exact five-gate invariant', () => {
    mocks.authSnapshot.value = {
      status: 'authenticated',
      user: { access_token: 'token', profile: { capability: 'result.record' } }
    }
    const wrapper = mount(QcWorkbenchView)

    expect(wrapper.text()).toContain('qc.manage')
    expect(wrapper.text()).toContain('五个放行门')
    expect(wrapper.findAll('form')[0]!.findAll('input, select, textarea, button')
      .every(control => control.attributes('disabled') !== undefined)).toBe(true)
  })

  it('preserves a Report lookup and retries explicitly after a network failure', async () => {
    mocks.report.get
      .mockRejectedValueOnce(new LabApiError(
        'WEB.NETWORK_ERROR', 0, 'corr-report', 'offline', 'retry explicitly'
      ))
      .mockResolvedValueOnce(reportResult())
    const wrapper = mount(ReportWorkbenchView)
    const lookup = wrapper.findAll('form')[1]!
    await lookup.findAll('input')[0]!.setValue('report-1')
    await lookup.trigger('submit')
    await flushPromises()
    expect(wrapper.text()).toContain('corr-report')

    const retry = wrapper.findAll('button').find(button => button.text() === '显式重试')
    expect(retry).toBeDefined()
    await retry!.trigger('click')
    await flushPromises()
    expect(mocks.report.get).toHaveBeenCalledTimes(2)
    expect(mocks.report.get.mock.calls[1]?.[0]).toBe('report-1')
    expect(wrapper.text()).toContain('Report 详情')
  })

  it('creates a version-bound Report delivery and downloads only by grant token', async () => {
    const wrapper = mount(ReportWorkbenchView)
    const operationForm = wrapper.findAll('form')[0]!
    await operationForm.get('select').setValue('delivery')
    await flushPromises()
    const operationInputs = operationForm.findAll('input')
    await operationInputs[0]!.setValue('report-1')
    await operationInputs[1]!.setValue('1')
    await operationForm.trigger('submit')
    await flushPromises()

    expect(mocks.report.delivery).toHaveBeenCalledWith(
      'report-1', 1,
      expect.objectContaining({
        ruleSetVersion: 'RPT-DELIVERY@1.0.0', recipientId: 'recipient-id', channel: 'PORTAL'
      }),
      { accessToken: 'token' }
    )

    const lookup = wrapper.findAll('form')[1]!
    await lookup.findAll('input')[4]!.setValue('grant-token-1')
    await lookup.findAll('button').find(button => button.text() === '下载固定版本')!.trigger('click')
    await flushPromises()
    expect(mocks.report.download).toHaveBeenCalledWith('grant-token-1', { accessToken: 'token' })
  })

  it('hides all business forms from anonymous sessions and preserves a safe sign-in return path', async () => {
    mocks.authSnapshot.value = { status: 'anonymous' }
    const wrapper = mount(InstrumentWorkbenchView)

    expect(wrapper.findAll('form')).toHaveLength(0)
    await wrapper.get('button').trigger('click')
    expect(mocks.signIn).toHaveBeenCalledWith('/workbench/second-flow')
  })
})

function instrumentResult() {
  return {
    fileRegistrationId: 'instrument-file-1', version: 1, state: 'INGESTED',
    ruleSetVersion: 'INST-IMPORT@1.0.0',
    objectScope: { legalEntityId: 'legal', laboratoryId: 'lab' },
    externalRef: { id: 'external', version: 1 }, sha256: 'a'.repeat(64),
    sourceSystem: 'INSTRUMENT', instrumentRef: { id: 'instrument', version: 1 },
    parserVersion: 'parser@1.0.0', declaredRowCount: 1, rows: [], exceptions: [],
    registeredBy: 'actor', registeredAt: '2026-07-29T00:00:00Z'
  }
}

function qcResult() {
  return {
    qcRunId: 'qc-1', version: 1, state: 'OPEN', ruleSetVersion: 'QC-IMPACT@1.0.0',
    objectScope: { legalEntityId: 'legal', laboratoryId: 'lab' },
    batchId: 'batch-1', batchVersion: 1, batchGateDecision: 'ALLOWED',
    batchGateRuleSetVersion: 'BATCH-EXECUTION@1.0.0',
    method: { id: 'method', version: 1 }, qcRuleSet: { id: 'qc-rule', version: 1 },
    results: [], impact: [], gates: [], deviationApprovals: [],
    openedBy: 'actor', openedAt: '2026-07-29T00:00:00Z'
  }
}

function reportResult() {
  return {
    reportId: 'report-1', version: 1, state: 'DRAFT', ruleSetVersion: 'RPT-ISSUANCE@1.0.0',
    objectScope: {
      legalEntityId: 'legal', laboratoryId: 'lab', customerId: 'customer',
      serviceOrderId: 'order', productCategory: 'TOYS'
    },
    reportNumber: 'REPORT-1', lines: [], gateEvaluations: [],
    createdBy: 'actor', createdAt: '2026-07-29T00:00:00Z'
  }
}

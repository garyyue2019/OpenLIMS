import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { LabelingApiError } from '../receiving/labeling-client'

const mocks = vi.hoisted(() => ({
  authSnapshot: {
    value: {
      status: 'authenticated',
      user: {
        access_token: 'token',
        profile: {
          capability: [
            'billing.record', 'receiving.label.print',
            'receiving.label.scan', 'receiving.label.reprint'
          ]
        }
      }
    } as Record<string, unknown>
  },
  signIn: vi.fn(),
  billing: {
    create: vi.fn(), adjustment: vi.fn(), get: vi.fn(), status: vi.fn(),
    export: vi.fn(), exportGet: vi.fn(), handoff: vi.fn(), handoffGet: vi.fn(),
    attempt: vi.fn(), differences: vi.fn()
  },
  labeling: { create: vi.fn(), get: vi.fn(), reprint: vi.fn(), scan: vi.fn() },
  idempotency: vi.fn(() => 'idem-web')
}))

vi.mock('../../auth-store', () => ({
  authSnapshot: mocks.authSnapshot,
  signIn: mocks.signIn
}))
vi.mock('vue-router', () => ({ useRoute: () => ({ fullPath: '/workbench/business' }) }))
vi.mock('./billing-client', async importOriginal => ({
  ...await importOriginal<typeof import('./billing-client')>(),
  createBillingEvidence: mocks.billing.create,
  addBillingAdjustment: mocks.billing.adjustment,
  getBillingEvidence: mocks.billing.get,
  getBillingEvidenceStatus: mocks.billing.status,
  createBillingExportBatch: mocks.billing.export,
  getBillingExportBatch: mocks.billing.exportGet,
  createBillingHandoff: mocks.billing.handoff,
  getBillingHandoff: mocks.billing.handoffGet,
  recordBillingHandoffAttempt: mocks.billing.attempt,
  getBillingDifferenceQueue: mocks.billing.differences
}))
vi.mock('../receiving/labeling-client', async importOriginal => ({
  ...await importOriginal<typeof import('../receiving/labeling-client')>(),
  createLabelJobs: mocks.labeling.create,
  getLabelJob: mocks.labeling.get,
  reprintLabel: mocks.labeling.reprint,
  resolveLabelScan: mocks.labeling.scan
}))
vi.mock('../receiving/receiving-client', async importOriginal => ({
  ...await importOriginal<typeof import('../receiving/receiving-client')>(),
  createIdempotencyKey: mocks.idempotency
}))

import BillingWorkbenchView from './BillingWorkbenchView.vue'
import LabelingWorkbenchView from './LabelingWorkbenchView.vue'

beforeEach(() => {
  vi.clearAllMocks()
  mocks.authSnapshot.value = {
    status: 'authenticated',
    user: {
      access_token: 'token',
      profile: {
        capability: [
          'billing.record', 'receiving.label.print',
          'receiving.label.scan', 'receiving.label.reprint'
        ]
      }
    }
  }
  mocks.billing.create.mockResolvedValue(billingEvidence())
  mocks.billing.status.mockResolvedValue({
    decision: 'UNKNOWN', reasonCodes: ['BILLING_UNAVAILABLE'],
    billingEvidenceId: 'billing-1', ruleSetVersion: 'BILLING-EVIDENCE@1.0.0'
  })
  mocks.billing.export.mockResolvedValue({ exportBatchId: 'export-1', state: 'CREATED' })
  mocks.billing.handoff.mockResolvedValue({ handoffId: 'handoff-1', state: 'PENDING' })
  mocks.billing.differences.mockResolvedValue({ externalSystem: 'ERP', items: [] })
  mocks.labeling.create.mockResolvedValue({ jobs: [labelJob()] })
  mocks.labeling.reprint.mockResolvedValue({ jobs: [{ ...labelJob(), printJobId: 'job-2', isReprint: true }] })
  mocks.labeling.scan.mockResolvedValue({
    objectType: 'RI', objectId: 'item-1', businessNumber: 'LAB-RI-1',
    state: 'QUARANTINED', printVerificationStatus: 'VERIFIED', allowedActions: []
  })
  mocks.labeling.get.mockResolvedValue(labelJob())
})

describe('Billing and Labeling workbench views', () => {
  it('creates Billing evidence and rejects zero amount without its required reason locally', async () => {
    const wrapper = mount(BillingWorkbenchView)
    await wrapper.findAll('form')[0]!.trigger('submit')
    await flushPromises()

    expect(mocks.billing.create).toHaveBeenCalledWith(
      expect.objectContaining({
        ruleSetVersion: 'BILLING-EVIDENCE@1.0.0',
        expectedGroupVersion: 1,
        amount: 120.5
      }),
      { accessToken: 'token' }
    )
    expect(wrapper.text()).toContain('billing-1')

    const textarea = wrapper.get('textarea')
    const invalid = JSON.parse(textarea.element.value)
    invalid.amount = 0
    await textarea.setValue(JSON.stringify(invalid))
    await wrapper.findAll('form')[0]!.trigger('submit')

    expect(mocks.billing.create).toHaveBeenCalledTimes(1)
    expect(wrapper.text()).toContain('零金额必须且只能填写原因')
  })

  it('shows server-owned UNKNOWN Billing status as blocked without inventing eligibility', async () => {
    const wrapper = mount(BillingWorkbenchView)
    const lookup = wrapper.findAll('form')[1]!
    await lookup.get('select').setValue('status')
    await flushPromises()
    await lookup.get('input').setValue('billing-1')
    await lookup.trigger('submit')
    await flushPromises()

    expect(mocks.billing.status).toHaveBeenCalledWith('billing-1', { accessToken: 'token' })
    expect(wrapper.text()).toContain('UNKNOWN')
    expect(wrapper.text()).toContain('BILLING_UNAVAILABLE')
  })

  it('creates an export and handoff, then reads the ERP difference queue', async () => {
    const wrapper = mount(BillingWorkbenchView)
    const operationForm = wrapper.findAll('form')[0]!
    await operationForm.get('select').setValue('export')
    await flushPromises()
    await operationForm.trigger('submit')
    await flushPromises()
    expect(mocks.billing.export).toHaveBeenCalledWith(
      expect.objectContaining({
        ruleSetVersion: 'BILLING-EXPORT@1.0.0', billingEvidenceIds: ['billing-evidence-id']
      }),
      { accessToken: 'token' }
    )

    await operationForm.get('select').setValue('handoff')
    await flushPromises()
    await operationForm.trigger('submit')
    await flushPromises()
    expect(mocks.billing.handoff).toHaveBeenCalledWith(
      'export-1',
      expect.objectContaining({ ruleSetVersion: 'BILLING-HANDOFF@1.0.0', externalSystem: 'ERP' }),
      { accessToken: 'token' }
    )

    const lookup = wrapper.findAll('form')[1]!
    await lookup.get('select').setValue('differences')
    await flushPromises()
    await lookup.findAll('select')[1]!.setValue('ERP')
    await lookup.trigger('submit')
    await flushPromises()
    expect(mocks.billing.differences).toHaveBeenCalledWith('ERP', { accessToken: 'token' })
  })

  it('executes create, controlled reprint, scan, and job lookup with stable idempotency', async () => {
    const wrapper = mount(LabelingWorkbenchView)
    const operationForm = wrapper.findAll('form')[0]!
    await operationForm.trigger('submit')
    await flushPromises()
    expect(mocks.labeling.create).toHaveBeenCalledWith(
      expect.objectContaining({
        printerId: 'receiving-lab-a',
        targets: [expect.objectContaining({ objectType: 'RI', objectVersion: 1 })]
      }),
      'token',
      'idem-web'
    )

    await operationForm.get('select').setValue('reprint')
    await flushPromises()
    await operationForm.trigger('submit')
    await flushPromises()
    expect(mocks.labeling.reprint).toHaveBeenCalledWith(
      'job-1', 'receiving-lab-a', 'Controlled reprint after damaged label.',
      'token', 'idem-web'
    )

    await operationForm.get('select').setValue('scan')
    await flushPromises()
    await operationForm.trigger('submit')
    await flushPromises()
    expect(mocks.labeling.scan).toHaveBeenCalledWith(
      'OL1:RI:opaque-reference:checksum', 'token'
    )

    const lookup = wrapper.findAll('form')[1]!
    await lookup.get('input').setValue('job-1')
    await lookup.trigger('submit')
    await flushPromises()
    expect(mocks.labeling.get).toHaveBeenCalledWith('job-1', 'token')
  })

  it('disables a missing capability and preserves correlation for explicit retry', async () => {
    mocks.authSnapshot.value = {
      status: 'authenticated',
      user: { access_token: 'token', profile: { capability: ['receiving.label.scan'] } }
    }
    const wrapper = mount(LabelingWorkbenchView)
    expect(wrapper.text()).toContain('当前身份没有 receiving.label.print 能力')
    expect(wrapper.findAll('form')[0]!.get('button[type="submit"]').attributes('disabled')).toBeDefined()

    await wrapper.findAll('form')[0]!.get('select').setValue('scan')
    mocks.labeling.scan
      .mockRejectedValueOnce(new LabelingApiError(
        'WEB.NETWORK_ERROR', 0, 'corr-label', 'offline', 'retry explicitly'
      ))
      .mockResolvedValueOnce({
        objectType: 'RI', objectId: 'item-1', businessNumber: 'LAB-RI-1',
        state: 'QUARANTINED', printVerificationStatus: 'VERIFIED', allowedActions: []
      })
    await wrapper.findAll('form')[0]!.trigger('submit')
    await flushPromises()
    expect(wrapper.text()).toContain('corr-label')

    const retry = wrapper.findAll('button').find(button => button.text() === '显式重试')
    await retry!.trigger('click')
    await flushPromises()
    expect(mocks.labeling.scan).toHaveBeenCalledTimes(2)
  })
})

function billingEvidence() {
  return {
    billingEvidenceId: 'billing-1', stage: 'BILLABLE_CANDIDATE',
    ruleSetVersion: 'BILLING-EVIDENCE@1.0.0',
    objectScope: {
      legalEntityId: 'legal', laboratoryId: 'lab', customerId: 'customer',
      serviceOrderId: 'order', productCategory: 'TOYS'
    },
    resultGroupId: 'result-group-id', groupVersion: 1, adoptionTargetId: 'adoption-1',
    contractBaseline: { id: 'contract', version: 1 }, chargeDimension: 'TEST',
    billingRuleVersion: 'PRICE@1.0.0', amount: 120.5, currency: { id: 'CNY', version: 1 },
    adjustments: [], recordedBy: 'server-actor', recordedAt: '2026-08-05T00:00:00Z'
  }
}

function labelJob() {
  return {
    printJobId: 'job-1', objectType: 'RI', objectId: 'item-1', businessNumber: 'LAB-RI-1',
    templateVersion: 'REC-RI-50X30@1.0.0', printerId: 'receiving-lab-a',
    status: 'DISPATCHED', isReprint: false, successfulReprintCount: 0,
    createdAt: '2026-08-05T00:00:00Z', updatedAt: '2026-08-05T00:00:00Z'
  }
}

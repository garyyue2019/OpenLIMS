import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { LabApiError } from '../lab-workbench/lab-api'

const mocks = vi.hoisted(() => ({
  authSnapshot: {
    value: {
      status: 'authenticated',
      user: {
        access_token: 'token',
        profile: { capability: ['commercial:write', 'operations:write', 'ai.run', 'ai.review'] }
      }
    } as Record<string, unknown>
  },
  signIn: vi.fn(),
  commercial: {
    catalogCreate: vi.fn(), catalogRevise: vi.fn(), catalogGet: vi.fn(),
    inquiryCreate: vi.fn(), inquiryGet: vi.fn(), gap: vi.fn(), capability: vi.fn(),
    quote: vi.fn(), impact: vi.fn()
  },
  operations: {
    lineageCreate: vi.fn(), lineageGet: vi.fn(), custodyCreate: vi.fn(), custodyGet: vi.fn(),
    planCreate: vi.fn(), planGet: vi.fn(), taskState: vi.fn(), reserve: vi.fn(), queue: vi.fn()
  },
  ai: { create: vi.fn(), get: vi.fn(), disposition: vi.fn(), queue: vi.fn() }
}))

vi.mock('../../auth-store', () => ({ authSnapshot: mocks.authSnapshot, signIn: mocks.signIn }))
vi.mock('vue-router', () => ({ useRoute: () => ({ fullPath: '/workbench/release' }) }))
vi.mock('./commercial-client', async importOriginal => ({
  ...await importOriginal<typeof import('./commercial-client')>(),
  createCatalogRecord: mocks.commercial.catalogCreate,
  reviseCatalogRecord: mocks.commercial.catalogRevise,
  getCatalogRecord: mocks.commercial.catalogGet,
  createInquiry: mocks.commercial.inquiryCreate,
  getInquiry: mocks.commercial.inquiryGet,
  resolveInquiryGap: mocks.commercial.gap,
  recordCapabilityReview: mocks.commercial.capability,
  createQuoteVersion: mocks.commercial.quote,
  recordCommercialChangeImpact: mocks.commercial.impact
}))
vi.mock('./operations-client', async importOriginal => ({
  ...await importOriginal<typeof import('./operations-client')>(),
  createLineageEdge: mocks.operations.lineageCreate,
  getSampleLineage: mocks.operations.lineageGet,
  recordCustodyEvent: mocks.operations.custodyCreate,
  getCustodyChain: mocks.operations.custodyGet,
  createWorkPlan: mocks.operations.planCreate,
  getWorkPlan: mocks.operations.planGet,
  changeWorkTaskState: mocks.operations.taskState,
  reserveWorkResource: mocks.operations.reserve,
  getWorkQueue: mocks.operations.queue
}))
vi.mock('./ai-client', async importOriginal => ({
  ...await importOriginal<typeof import('./ai-client')>(),
  createAiRun: mocks.ai.create,
  getAiRun: mocks.ai.get,
  recordAiDisposition: mocks.ai.disposition,
  getAiReviewQueue: mocks.ai.queue
}))

import AiReviewWorkbenchView from './AiReviewWorkbenchView.vue'
import CommercialWorkbenchView from './CommercialWorkbenchView.vue'
import OperationsWorkbenchView from './OperationsWorkbenchView.vue'

beforeEach(() => {
  vi.clearAllMocks()
  mocks.authSnapshot.value = {
    status: 'authenticated',
    user: {
      access_token: 'token',
      profile: { capability: ['commercial:write', 'operations:write', 'ai.run', 'ai.review'] }
    }
  }
  mocks.commercial.inquiryCreate.mockResolvedValue(inquiryResult())
  mocks.commercial.inquiryGet.mockResolvedValue(inquiryResult())
  mocks.operations.planCreate.mockResolvedValue(workPlanResult())
  mocks.operations.queue.mockResolvedValue({
    workCenterId: 'center-1', state: 'READY', ruleSetVersion: 'OPERATIONS@1.0.0', items: []
  })
  mocks.ai.create.mockResolvedValue(aiRunResult())
  mocks.ai.queue.mockResolvedValue({ runs: [aiRunResult()], ruleSetVersion: 'AI-RUNTIME@1.0.0' })
  mocks.ai.disposition.mockResolvedValue({ runId: 'run-1', version: 2 })
})

describe('release workbench views', () => {
  it('creates and reloads a Commercial inquiry with server-owned gaps', async () => {
    const wrapper = mount(CommercialWorkbenchView)
    expect(wrapper.text()).toContain('尚未加载商业对象')

    await wrapper.findAll('form')[0]!.trigger('submit')
    await flushPromises()
    expect(mocks.commercial.inquiryCreate).toHaveBeenCalledWith(
      expect.objectContaining({
        details: expect.objectContaining({ productCategory: 'TOYS', sourceDocuments: [{ id: 'versioned-ref-id', version: 1 }] })
      }),
      { accessToken: 'token' }
    )
    expect(wrapper.text()).toContain('inquiry-1')

    await wrapper.findAll('form')[1]!.trigger('submit')
    await flushPromises()
    expect(mocks.commercial.inquiryGet).toHaveBeenCalledWith('inquiry-1', { accessToken: 'token' })
  })

  it('loads an Operations queue and retries explicitly after a network failure', async () => {
    mocks.operations.queue
      .mockRejectedValueOnce(new LabApiError(
        'WEB.NETWORK_ERROR', 0, 'corr-operations', 'offline', 'retry explicitly'
      ))
      .mockResolvedValueOnce({
        workCenterId: 'center-1', state: 'READY', ruleSetVersion: 'OPERATIONS@1.0.0', items: []
      })
    const wrapper = mount(OperationsWorkbenchView)
    const lookup = wrapper.findAll('form')[1]!
    const inputs = lookup.findAll('input')
    await inputs[0]!.setValue('center-1')
    await inputs[1]!.setValue('READY')
    await lookup.trigger('submit')
    await flushPromises()

    expect(wrapper.text()).toContain('corr-operations')
    await wrapper.findAll('button').find(button => button.text() === '显式重试')!.trigger('click')
    await flushPromises()
    expect(mocks.operations.queue).toHaveBeenCalledTimes(2)
    expect(mocks.operations.queue).toHaveBeenLastCalledWith('center-1', 'READY', { accessToken: 'token' })
    expect(wrapper.text()).toContain('工作队列')
  })

  it('records a provider-disabled AI run and reads the review queue separately', async () => {
    const wrapper = mount(AiReviewWorkbenchView)
    await wrapper.findAll('form')[0]!.trigger('submit')
    await flushPromises()

    expect(mocks.ai.create).toHaveBeenCalledWith(
      expect.objectContaining({
        ruleSetVersion: 'AI-RUNTIME@1.0.0', idempotencyKey: 'ai-run-idempotency-key'
      }),
      { accessToken: 'token' }
    )
    expect(wrapper.text()).toContain('PROVIDER_DISABLED')

    const lookup = wrapper.findAll('form')[1]!
    await lookup.get('select').setValue('QUARANTINED')
    await lookup.findAll('button').find(button => button.text() === '加载复核队列')!.trigger('click')
    await flushPromises()
    expect(mocks.ai.queue).toHaveBeenCalledWith('QUARANTINED', { accessToken: 'token' })
  })

  it('enables human disposition with ai.review while keeping ai.run disabled', async () => {
    mocks.authSnapshot.value = {
      status: 'authenticated',
      user: { access_token: 'token', profile: { capability: ['ai.review'] } }
    }
    const wrapper = mount(AiReviewWorkbenchView)
    expect(wrapper.findAll('form')[0]!.get('button[type="submit"]').attributes('disabled')).toBeDefined()

    await wrapper.findAll('form')[0]!.get('select').setValue('disposition')
    await flushPromises()
    await wrapper.findAll('form')[0]!.get('input').setValue('run-1')
    await wrapper.findAll('form')[0]!.trigger('submit')
    await flushPromises()

    expect(mocks.ai.disposition).toHaveBeenCalledWith(
      'run-1',
      expect.objectContaining({ kind: 'MODIFY', humanValue: '人工确认值' }),
      { accessToken: 'token' }
    )
  })
})

function inquiryResult() {
  return {
    inquiryId: 'inquiry-1', inquiryNumber: 'INQ-1', version: 1,
    ruleSetVersion: 'COMMERCIAL@1.0.0', state: 'GAPS_OPEN',
    details: { productCategory: 'TOYS', sourceDocuments: [] },
    objectScope: {
      legalEntityId: 'legal', laboratoryId: 'lab', customerId: 'customer',
      serviceOrderId: 'order', productCategory: 'TOYS'
    },
    gaps: [{ gapId: 'gap-1' }], capabilityReviews: [], quoteVersions: [], changeImpacts: [],
    recordedBy: 'actor', recordedAt: '2026-08-05T00:00:00Z'
  }
}

function workPlanResult() {
  return {
    workPlanId: 'plan-1', version: 1, ruleSetVersion: 'OPERATIONS@1.0.0', state: 'PLANNED',
    objectScope: {
      legalEntityId: 'legal', laboratoryId: 'lab', customerId: 'customer',
      serviceOrderId: 'order', productCategory: 'TOYS'
    },
    tasks: [], reservations: []
  }
}

function aiRunResult() {
  return {
    runId: 'run-1', version: 1, status: 'PROVIDER_DISABLED',
    objectScope: {
      legalEntityId: 'legal', laboratoryId: 'lab', customerId: 'customer',
      serviceOrderId: 'order', productCategory: 'TOYS'
    },
    envelope: {
      model: { id: 'model', version: 1 }, gatewayRoute: 'disabled',
      promptTemplate: { id: 'prompt', version: 1 }, outputSchema: { id: 'schema', version: 1 }, inputRefs: []
    },
    providerStatus: 'DISABLED', dispositions: [], humanReviewRequired: false,
    manualFallbackRequired: true, ruleSetVersion: 'AI-RUNTIME@1.0.0'
  }
}

import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { LabApiError } from './lab-api'

const mocks = vi.hoisted(() => ({
  authSnapshot: {
    value: {
      status: 'authenticated',
      user: {
        access_token: 'token',
        profile: { capability: ['scope.approve', 'quantity.post', 'allocation.assign', 'batch.manage'] }
      }
    } as Record<string, unknown>
  },
  signIn: vi.fn(),
  scope: {
    create: vi.fn(), revise: vi.fn(), get: vi.fn(), eligibility: vi.fn()
  },
  quantity: {
    create: vi.fn(), post: vi.fn(), get: vi.fn(), availability: vi.fn()
  },
  allocation: {
    create: vi.fn(), release: vi.fn(), get: vi.fn(), status: vi.fn()
  },
  batch: {
    create: vi.fn(), member: vi.fn(), evidence: vi.fn(), freeze: vi.fn(), get: vi.fn(), status: vi.fn()
  }
}))

vi.mock('../../auth-store', () => ({
  authSnapshot: mocks.authSnapshot,
  signIn: mocks.signIn
}))
vi.mock('vue-router', () => ({ useRoute: () => ({ fullPath: '/workbench/test' }) }))
vi.mock('./scope-client', async (importOriginal) => ({
  ...await importOriginal<typeof import('./scope-client')>(),
  createScopeMatrix: mocks.scope.create,
  reviseScopeMatrix: mocks.scope.revise,
  getScopeMatrixVersion: mocks.scope.get,
  getScopeProductionEligibility: mocks.scope.eligibility
}))
vi.mock('./quantity-client', async (importOriginal) => ({
  ...await importOriginal<typeof import('./quantity-client')>(),
  createQuantityAccount: mocks.quantity.create,
  postQuantityEntry: mocks.quantity.post,
  getQuantityAccount: mocks.quantity.get,
  getQuantityAvailability: mocks.quantity.availability
}))
vi.mock('./allocation-client', async (importOriginal) => ({
  ...await importOriginal<typeof import('./allocation-client')>(),
  createTestObjectAllocation: mocks.allocation.create,
  releaseTestObjectAllocation: mocks.allocation.release,
  getTestObjectAllocation: mocks.allocation.get,
  getAllocationStatus: mocks.allocation.status
}))
vi.mock('./batch-client', async (importOriginal) => ({
  ...await importOriginal<typeof import('./batch-client')>(),
  createBatch: mocks.batch.create,
  addBatchMember: mocks.batch.member,
  addBatchEvidence: mocks.batch.evidence,
  freezeBatch: mocks.batch.freeze,
  getBatch: mocks.batch.get,
  getBatchStatus: mocks.batch.status
}))

import AllocationWorkbenchView from './AllocationWorkbenchView.vue'
import BatchWorkbenchView from './BatchWorkbenchView.vue'
import QuantityWorkbenchView from './QuantityWorkbenchView.vue'
import ScopeWorkbenchView from './ScopeWorkbenchView.vue'

beforeEach(() => {
  vi.clearAllMocks()
  mocks.authSnapshot.value = {
    status: 'authenticated',
    user: {
      access_token: 'token',
      profile: { capability: ['scope.approve', 'quantity.post', 'allocation.assign', 'batch.manage'] }
    }
  }
  mocks.scope.get.mockResolvedValue(scopeResult())
  mocks.quantity.get.mockResolvedValue(quantityResult())
  mocks.allocation.get.mockResolvedValue(allocationResult())
  mocks.batch.create.mockResolvedValue(batchResult())
})

describe('laboratory workbench views', () => {
  it('loads a Scope detail by stable id and exact version, while rejecting an empty create form', async () => {
    const wrapper = mount(ScopeWorkbenchView)
    await wrapper.findAll('form')[0]!.trigger('submit')
    expect(mocks.scope.create).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain('创建版本固定为 0')

    const lookup = wrapper.findAll('form')[1]!
    const inputs = lookup.findAll('input')
    await inputs[0]!.setValue('scope-1')
    await inputs[1]!.setValue('2')
    await lookup.trigger('submit')
    await flushPromises()

    expect(mocks.scope.get).toHaveBeenCalledWith('scope-1', 2, { accessToken: 'token' })
    expect(wrapper.text()).toContain('范围版本详情')
    expect(wrapper.html()).not.toContain('organizationGroupId')
    expect(wrapper.html()).not.toContain('actorId')
  })

  it('validates Quantity boundaries and exposes explicit retry after a network failure', async () => {
    mocks.quantity.get
      .mockRejectedValueOnce(new LabApiError(
        'WEB.NETWORK_ERROR', 0, 'corr-network', 'offline', 'retry explicitly'
      ))
      .mockResolvedValueOnce(quantityResult())
    const wrapper = mount(QuantityWorkbenchView)

    await wrapper.findAll('form')[1]!.trigger('submit')
    expect(mocks.quantity.post).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain('大于 0 的数量')

    const lookup = wrapper.findAll('form')[2]!
    await lookup.find('input').setValue('quantity-1')
    await lookup.trigger('submit')
    await flushPromises()
    expect(wrapper.text()).toContain('corr-network')

    const retryButton = wrapper.findAll('button').find(button => button.text() === '显式重试')
    expect(retryButton).toBeDefined()
    await retryButton!.trigger('click')
    await flushPromises()
    expect(mocks.quantity.get).toHaveBeenCalledTimes(2)
    expect(wrapper.text()).toContain('数量账户')
  })

  it('keeps Allocation writes disabled without exact capability and never renders trusted identity inputs', () => {
    mocks.authSnapshot.value = {
      status: 'authenticated',
      user: { access_token: 'token', profile: { capability: 'scope.approve' } }
    }
    const wrapper = mount(AllocationWorkbenchView)

    expect(wrapper.text()).toContain('allocation.assign')
    expect(wrapper.findAll('form')[0]!.findAll('input').every(input => input.attributes('disabled') !== undefined)).toBe(true)
    expect(wrapper.html()).not.toContain('organizationGroupId')
    expect(wrapper.html()).not.toContain('actorId')
  })

  it('creates a typed Batch and rejects malformed evidence before any request', async () => {
    const wrapper = mount(BatchWorkbenchView)
    const create = wrapper.findAll('form')[0]!
    const inputs = create.findAll('input')
    await inputs[0]!.setValue('legal-a')
    await inputs[1]!.setValue('lab-a')
    await create.trigger('submit')
    await flushPromises()

    expect(mocks.batch.create).toHaveBeenCalledWith({
      ruleSetVersion: 'BATCH-EXECUTION@1.0.0',
      objectScope: { legalEntityId: 'legal-a', laboratoryId: 'lab-a' },
      batchType: 'ANALYTICAL'
    }, { accessToken: 'token' })
    expect(wrapper.text()).toContain('batch-1')

    await wrapper.findAll('form')[2]!.trigger('submit')
    expect(mocks.batch.evidence).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain('64 位十六进制 SHA-256')
  })

  it('hides business forms from anonymous sessions and preserves a safe sign-in return path', async () => {
    mocks.authSnapshot.value = { status: 'anonymous' }
    const wrapper = mount(ScopeWorkbenchView)

    expect(wrapper.findAll('form')).toHaveLength(0)
    await wrapper.get('button').trigger('click')
    expect(mocks.signIn).toHaveBeenCalledWith('/workbench/test')
  })
})

function scopeResult() {
  return {
    scopeMatrixId: 'scope-1', version: 2, state: 'APPROVED', ruleSetVersion: 'SCOPE-LINE-GATE@1.0.0',
    objectScope: { legalEntityId: 'legal', laboratoryId: 'lab', customerId: 'customer', serviceOrderId: 'order', productCategory: 'TOYS' },
    lines: [{
      scopeLineId: 'line-1', subjectType: 'FEATURE_NODE', subject: { id: 'feature', version: 1 },
      targetMarket: { id: 'market', version: 1 }, requirementClause: { id: 'requirement', version: 1 },
      testItem: { id: 'item', version: 1 }, method: { id: 'method', version: 1 }, methodOption: 'A',
      sampleRequirement: { id: 'sample', version: 1 }, evaluationMode: 'EVALUATED',
      workCenter: { id: 'work-center', version: 1 }, reportPosition: '1'
    }], approvedBy: 'actor', approvedAt: '2026-07-29T00:00:00Z'
  }
}

function quantityResult() {
  return {
    quantityAccountId: 'quantity-1', version: 2, ruleSetVersion: 'SAMPLE-QUANTITY@1.0.0',
    objectScope: { legalEntityId: 'legal', laboratoryId: 'lab', customerId: 'customer', serviceOrderId: 'order', productCategory: 'TOYS' },
    subject: { subjectType: 'RECEIVED_ITEM', id: 'item-1', version: 1 },
    dimension: 'MASS', unit: 'GRAM', precisionScale: 2, conservationTolerance: 0,
    balance: 10, reserved: 2, available: 8, createdBy: 'actor', createdAt: '2026-07-29T00:00:00Z'
  }
}

function allocationResult() {
  const gate = { source: 'SCOPE', decision: 'ALLOWED', pinnedVersion: 1, ruleSetVersion: 'rule@1.0.0', reasonCodes: [] }
  return {
    allocationId: 'allocation-1', state: 'ACTIVE', subjectAllocationVersion: 1,
    ruleSetVersion: 'TASK-ALLOCATION@1.0.0',
    objectScope: { legalEntityId: 'legal', laboratoryId: 'lab', customerId: 'customer', serviceOrderId: 'order', productCategory: 'TOYS' },
    subject: { subjectType: 'RECEIVED_ITEM', id: 'item-1', version: 1 },
    identityAssignment: { id: 'identity-1', version: 1 }, scopeMatrixId: 'scope-1', scopeLineId: 'line-1',
    planStep: { id: 'plan-1', version: 1 }, purpose: 'test', sequenceOrder: 1, destructive: false,
    quantityAccountId: 'quantity-1', requestedAmount: 1, dimension: 'MASS', unit: 'GRAM',
    storageCondition: { id: 'storage-1', version: 1 }, validUntil: '2026-08-02T00:00:00Z',
    receivingGate: { ...gate, source: 'RECEIVING' }, scopeGate: gate, quantityGate: { ...gate, source: 'QUANTITY' },
    assignedBy: 'actor', assignedAt: '2026-07-29T00:00:00Z'
  }
}

function batchResult() {
  return {
    batchId: 'batch-1', batchType: 'ANALYTICAL', state: 'ACTIVE', version: 1,
    ruleSetVersion: 'BATCH-EXECUTION@1.0.0', objectScope: { legalEntityId: 'legal-a', laboratoryId: 'lab-a' },
    members: [], evidence: [], createdBy: 'actor', createdAt: '2026-07-29T00:00:00Z'
  }
}

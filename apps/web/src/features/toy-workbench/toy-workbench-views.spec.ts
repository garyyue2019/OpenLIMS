import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { LabApiError } from '../lab-workbench/lab-api'

const allCapabilities = [
  'toy.manage', 'toy.sample-demand.approve', 'toy.label.manage', 'toy.label.review',
  'toy.conclusion.approve-item', 'toy.conclusion.approve-scope'
]
const mocks = vi.hoisted(() => ({
  authSnapshot: { value: {} as Record<string, unknown> },
  signIn: vi.fn(),
  product: { declaration: vi.fn(), decision: vi.fn(), freeze: vi.fn(), assessment: vi.fn(), resolve: vi.fn(), get: vi.fn() },
  testUnit: { create: vi.fn(), approve: vi.fn(), allocation: vi.fn(), get: vi.fn() },
  label: { artifact: vi.fn(), version: vi.fn(), review: vi.fn(), decision: vi.fn(), status: vi.fn() },
  conclusion: { item: vi.fn(), scope: vi.fn(), get: vi.fn(), list: vi.fn() }
}))

vi.mock('../../auth-store', () => ({ authSnapshot: mocks.authSnapshot, signIn: mocks.signIn }))
vi.mock('vue-router', () => ({ useRoute: () => ({ fullPath: '/workbench/toy' }) }))
vi.mock('./toy-product-client', async importOriginal => ({
  ...await importOriginal<typeof import('./toy-product-client')>(),
  recordToyAgeDeclaration: mocks.product.declaration, recordToyAgeGradeDecision: mocks.product.decision,
  freezeToyAgeGradeDecision: mocks.product.freeze, recordToyAccessibilityAssessment: mocks.product.assessment,
  resolveToyReassessmentTrigger: mocks.product.resolve, getToyProductOverview: mocks.product.get
}))
vi.mock('./toy-test-unit-client', async importOriginal => ({
  ...await importOriginal<typeof import('./toy-test-unit-client')>(),
  createToyTestUnitPlan: mocks.testUnit.create, approveToySampleRequirement: mocks.testUnit.approve,
  requestToyAllocation: mocks.testUnit.allocation, getToyTestUnitPlan: mocks.testUnit.get
}))
vi.mock('./toy-label-review-client', async importOriginal => ({
  ...await importOriginal<typeof import('./toy-label-review-client')>(),
  createToyLabelArtifact: mocks.label.artifact, appendToyLabelArtifactVersion: mocks.label.version,
  createToyLabelReview: mocks.label.review, decideToyLabelReview: mocks.label.decision,
  getToyLabelReviewStatus: mocks.label.status
}))
vi.mock('./toy-conclusion-client', async importOriginal => ({
  ...await importOriginal<typeof import('./toy-conclusion-client')>(),
  createToyItemConclusion: mocks.conclusion.item, createToyScopeConclusion: mocks.conclusion.scope,
  getToyConclusion: mocks.conclusion.get, getToyConclusionsByProduct: mocks.conclusion.list
}))

import ToyConclusionWorkbenchView from './ToyConclusionWorkbenchView.vue'
import ToyLabelReviewWorkbenchView from './ToyLabelReviewWorkbenchView.vue'
import ToyProductWorkbenchView from './ToyProductWorkbenchView.vue'
import ToyTestUnitWorkbenchView from './ToyTestUnitWorkbenchView.vue'

beforeEach(() => {
  vi.clearAllMocks()
  authenticate(allCapabilities)
  mocks.product.declaration.mockResolvedValue(product('SETTLED'))
  mocks.product.decision.mockResolvedValue(product('SETTLED'))
  mocks.product.freeze.mockResolvedValue(product('SETTLED'))
  mocks.product.assessment.mockResolvedValue(product('SETTLED'))
  mocks.product.resolve.mockResolvedValue(product('SETTLED'))
  mocks.product.get.mockResolvedValue(product('SETTLED'))
  mocks.testUnit.create.mockResolvedValue(plan('DRAFT', 'PENDING_TECHNICAL_APPROVAL'))
  mocks.testUnit.approve.mockResolvedValue(plan('APPROVED', 'APPROVED'))
  mocks.testUnit.allocation.mockResolvedValue(plan('APPROVED', 'APPROVED'))
  mocks.testUnit.get.mockResolvedValue(plan('APPROVED', 'APPROVED'))
  mocks.label.artifact.mockResolvedValue(artifact())
  mocks.label.version.mockResolvedValue(artifact())
  mocks.label.review.mockResolvedValue(review('DRAFT'))
  mocks.label.decision.mockResolvedValue(review('APPROVED'))
  mocks.label.status.mockResolvedValue(labelStatus('VALID'))
  mocks.conclusion.item.mockResolvedValue(conclusion('ITEM_CONFORMITY'))
  mocks.conclusion.scope.mockResolvedValue(conclusion('TESTED_SCOPE_CONFORMITY'))
  mocks.conclusion.get.mockResolvedValue(conclusion('ITEM_CONFORMITY'))
  mocks.conclusion.list.mockResolvedValue([conclusion('ITEM_CONFORMITY')])
})

describe('Toy workbench views', () => {
  it('records and reloads the product chain while rejecting an invalid accessibility stage locally', async () => {
    const wrapper = mount(ToyProductWorkbenchView)
    const write = wrapper.findAll('form')[0]!
    await write.find('input').setValue('product-1')
    await write.trigger('submit')
    await flushPromises()
    expect(mocks.product.declaration).toHaveBeenCalledWith(
      'product-1', expect.objectContaining({ expectedCurrentVersion: 0 }), { accessToken: 'token' }
    )

    await wrapper.findAll('form')[1]!.trigger('submit')
    await flushPromises()
    expect(mocks.product.get).toHaveBeenCalledWith('product-1', { accessToken: 'token' })

    await write.get('select').setValue('assessment')
    await flushPromises()
    const invalid = JSON.parse(wrapper.get('textarea').element.value)
    invalid.stage = 'UNKNOWN'
    await wrapper.get('textarea').setValue(JSON.stringify(invalid))
    await write.trigger('submit')
    expect(mocks.product.assessment).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain('批准阶段')
  })

  it('executes TestUnit create, approval, allocation, and query with separate capabilities', async () => {
    const wrapper = mount(ToyTestUnitWorkbenchView)
    const write = wrapper.findAll('form')[0]!
    await write.find('input').setValue('product-1')
    await write.trigger('submit')
    await flushPromises()
    expect(mocks.testUnit.create).toHaveBeenCalled()

    await write.get('select').setValue('approval')
    await flushPromises()
    await write.trigger('submit')
    await flushPromises()
    expect(mocks.testUnit.approve).toHaveBeenCalledWith(
      'product-1', 1, expect.objectContaining({ inputHash: 'server-plan-input-hash' }), { accessToken: 'token' }
    )

    await write.get('select').setValue('allocation')
    await flushPromises()
    await write.trigger('submit')
    await flushPromises()
    expect(mocks.testUnit.allocation).toHaveBeenCalled()
    await wrapper.findAll('form')[1]!.trigger('submit')
    await flushPromises()
    expect(mocks.testUnit.get).toHaveBeenCalledWith('product-1', 1, { accessToken: 'token' })
  })

  it('runs all label operations and shows UNKNOWN status as blocked', async () => {
    mocks.label.status.mockResolvedValue(labelStatus('UNKNOWN'))
    const wrapper = mount(ToyLabelReviewWorkbenchView)
    const write = wrapper.findAll('form')[0]!
    await write.find('input').setValue('product-1')
    await write.trigger('submit')
    await flushPromises()
    expect(mocks.label.artifact).toHaveBeenCalled()

    await write.get('select').setValue('version')
    await flushPromises()
    await write.trigger('submit')
    await flushPromises()
    expect(mocks.label.version).toHaveBeenCalledWith('product-1', 'artifact-1', expect.anything(), { accessToken: 'token' })

    await write.get('select').setValue('review')
    await flushPromises()
    await write.trigger('submit')
    await flushPromises()
    expect(mocks.label.review).toHaveBeenCalled()

    await write.get('select').setValue('decision')
    await flushPromises()
    await write.trigger('submit')
    await flushPromises()
    expect(mocks.label.decision).toHaveBeenCalledWith('product-1', 'review-1', expect.anything(), { accessToken: 'token' })

    await wrapper.findAll('form')[1]!.trigger('submit')
    await flushPromises()
    expect(mocks.label.status).toHaveBeenCalledWith(
      'product-1', expect.objectContaining({ productVersion: 1, artifactType: 'LABEL' }), { accessToken: 'token' }
    )
    expect(wrapper.text()).toContain('UNKNOWN')
    expect(wrapper.text()).toContain('LABEL_IMPACT_UNKNOWN')
  })

  it('creates both fixed conclusion levels and queries by id and product version', async () => {
    const wrapper = mount(ToyConclusionWorkbenchView)
    const write = wrapper.findAll('form')[0]!
    await write.trigger('submit')
    await flushPromises()
    expect(mocks.conclusion.item).toHaveBeenCalledWith(
      expect.not.objectContaining({ customStatement: expect.anything() }), { accessToken: 'token' }
    )

    await write.get('select').setValue('scope')
    await flushPromises()
    await write.trigger('submit')
    await flushPromises()
    expect(mocks.conclusion.scope).toHaveBeenCalledWith(
      expect.objectContaining({ uncoveredScopes: expect.any(Array), isFictitiousWholeItemConclusion: false }),
      { accessToken: 'token' }
    )

    const query = wrapper.findAll('form')[1]!
    await query.trigger('submit')
    await flushPromises()
    expect(mocks.conclusion.get).toHaveBeenCalledWith('conclusion-1', { accessToken: 'token' })
    await query.get('select').setValue('product')
    await flushPromises()
    const inputs = query.findAll('input')
    await inputs[0]!.setValue('product-1')
    await inputs[1]!.setValue('2')
    await query.trigger('submit')
    await flushPromises()
    expect(mocks.conclusion.list).toHaveBeenCalledWith('product-1', 2, { accessToken: 'token' })
  })

  it('separates all six capabilities at the action boundary', async () => {
    authenticate(['toy.manage', 'toy.label.review', 'toy.conclusion.approve-item'])
    const testUnit = mount(ToyTestUnitWorkbenchView)
    await testUnit.findAll('form')[0]!.get('select').setValue('approval')
    await flushPromises()
    expect(testUnit.text()).toContain('toy.sample-demand.approve')
    expect(testUnit.findAll('form')[0]!.get('button[type="submit"]').attributes('disabled')).toBeDefined()

    const label = mount(ToyLabelReviewWorkbenchView)
    expect(label.text()).toContain('toy.label.manage')
    await label.findAll('form')[0]!.get('select').setValue('decision')
    await flushPromises()
    expect(label.findAll('form')[0]!.get('button[type="submit"]').attributes('disabled')).toBeUndefined()

    const conclusions = mount(ToyConclusionWorkbenchView)
    await conclusions.findAll('form')[0]!.get('select').setValue('scope')
    await flushPromises()
    expect(conclusions.text()).toContain('toy.conclusion.approve-scope')
    expect(conclusions.findAll('form')[0]!.get('button[type="submit"]').attributes('disabled')).toBeDefined()
  })

  it('rejects invalid label hashes, custom wording, and whole-item scope conclusions locally', async () => {
    const label = mount(ToyLabelReviewWorkbenchView)
    await label.findAll('form')[0]!.find('input').setValue('product-1')
    const labelPayload = JSON.parse(label.get('textarea').element.value)
    labelPayload.contentHash = 'not-a-hash'
    await label.get('textarea').setValue(JSON.stringify(labelPayload))
    await label.findAll('form')[0]!.trigger('submit')
    expect(mocks.label.artifact).not.toHaveBeenCalled()
    expect(label.text()).toContain('SHA-256')

    const conclusions = mount(ToyConclusionWorkbenchView)
    const write = conclusions.findAll('form')[0]!
    const item = JSON.parse(conclusions.get('textarea').element.value)
    item.customStatement = '全部符合'
    await conclusions.get('textarea').setValue(JSON.stringify(item))
    await write.trigger('submit')
    expect(mocks.conclusion.item).not.toHaveBeenCalled()
    expect(conclusions.text()).toContain('禁止提交 customStatement')

    await write.get('select').setValue('scope')
    await flushPromises()
    const scopePayload = JSON.parse(conclusions.get('textarea').element.value)
    scopePayload.isFictitiousWholeItemConclusion = true
    await conclusions.get('textarea').setValue(JSON.stringify(scopePayload))
    await write.trigger('submit')
    expect(mocks.conclusion.scope).not.toHaveBeenCalled()
    expect(conclusions.text()).toContain('禁止虚构整件')
  })

  it('preserves exact query input and retries a network failure only after an explicit click', async () => {
    mocks.conclusion.get
      .mockRejectedValueOnce(new LabApiError('WEB.NETWORK_ERROR', 0, 'corr-toy', 'offline', 'retry explicitly'))
      .mockResolvedValueOnce(conclusion('ITEM_CONFORMITY'))
    const wrapper = mount(ToyConclusionWorkbenchView)
    const query = wrapper.findAll('form')[1]!
    await query.find('input').setValue('conclusion/1')
    await query.trigger('submit')
    await flushPromises()
    expect(wrapper.text()).toContain('corr-toy')
    expect(mocks.conclusion.get).toHaveBeenCalledTimes(1)

    const retry = wrapper.findAll('button').find(button => button.text() === '显式重试')
    await retry!.trigger('click')
    await flushPromises()
    expect(mocks.conclusion.get).toHaveBeenCalledTimes(2)
    expect(mocks.conclusion.get.mock.calls[1]?.[0]).toBe('conclusion/1')
  })
})

function authenticate(capabilities: string[]): void {
  mocks.authSnapshot.value = {
    status: 'authenticated', user: { access_token: 'token', profile: { capability: capabilities } }
  }
}
function product(accessibilityStatus: 'SETTLED' | 'REASSESSMENT_PENDING') {
  return {
    productId: 'product-1', version: 1, ruleSetVersion: 'TOY-AGE-GRADE@1.0.0',
    objectScope: { legalEntityId: 'legal', laboratoryId: 'lab' }, declarations: [], decisions: [],
    assessments: [], triggers: [], accessibilityStatus
  }
}
function plan(state: 'DRAFT' | 'APPROVED', decision: 'PENDING_TECHNICAL_APPROVAL' | 'APPROVED' | 'UNKNOWN') {
  return {
    planId: 'plan-1', productId: 'product-1', productVersion: 1, planVersion: 1,
    ruleSetVersion: 'TOY-TEST-UNIT-SAMPLE-DEMAND@1.0.0', state, inputHash: 'plan-hash',
    objectScope: { legalEntityId: 'legal', laboratoryId: 'lab' }, testUnits: [],
    requirement: { requirementId: 'requirement-1', requirementVersion: 1, decision, reasonCodes: [], inputHash: 'input-hash', ruleSetVersion: 'TOY-TEST-UNIT-SAMPLE-DEMAND@1.0.0' },
    downstreamDecisions: []
  }
}
function artifact() {
  return {
    artifactId: 'artifact-1', productId: 'product-1', artifactType: 'LABEL', language: 'zh-CN',
    market: 'CN', objectScope: { legalEntityId: 'legal', laboratoryId: 'lab' }, versions: [], currentVersion: 1
  }
}
function review(state: 'DRAFT' | 'APPROVED') {
  return {
    reviewId: 'review-1', productId: 'product-1', artifactId: 'artifact-1', artifactType: 'LABEL',
    objectScope: { legalEntityId: 'legal', laboratoryId: 'lab' }, versions: [{ reviewVersion: 1, state }], currentVersion: 1
  }
}
function labelStatus(decision: 'VALID' | 'UNKNOWN') {
  return {
    decision, reasonCodes: decision === 'UNKNOWN' ? ['LABEL_IMPACT_UNKNOWN'] : [],
    productId: 'product-1', artifactId: 'artifact-1', artifactVersion: 1,
    reviewId: 'review-1', reviewVersion: 1, productVersion: 1, ageGradeDecisionVersion: 1,
    ruleSetVersion: 'TOY-LABEL-REVIEW@1.0.0'
  }
}
function conclusion(level: 'ITEM_CONFORMITY' | 'TESTED_SCOPE_CONFORMITY') {
  return {
    conclusionId: 'conclusion-1', conclusionLevel: level, statement: 'server fixed statement',
    approvedBy: 'server-actor', approvedAt: '2026-08-05T00:00:00Z', version: 1
  }
}

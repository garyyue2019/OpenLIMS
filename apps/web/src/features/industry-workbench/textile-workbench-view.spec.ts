import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { LabApiError } from '../lab-workbench/lab-api'

const mocks = vi.hoisted(() => ({
  authSnapshot: {
    value: {
      status: 'authenticated',
      user: {
        access_token: 'token',
        profile: {
          capability: ['textile.sample-requirement.manage', 'textile.cutting-plan.approve']
        }
      }
    } as Record<string, unknown>
  },
  signIn: vi.fn(),
  calculate: vi.fn(),
  create: vi.fn(),
  approve: vi.fn(),
  get: vi.fn()
}))

vi.mock('../../auth-store', () => ({
  authSnapshot: mocks.authSnapshot,
  signIn: mocks.signIn
}))
vi.mock('vue-router', () => ({ useRoute: () => ({ fullPath: '/workbench/textile' }) }))
vi.mock('./textile-client', async importOriginal => ({
  ...await importOriginal<typeof import('./textile-client')>(),
  calculateTextileSampleRequirement: mocks.calculate,
  createTextileCuttingPlan: mocks.create,
  approveTextileCuttingPlan: mocks.approve,
  getTextileCuttingPlan: mocks.get
}))

import TextileWorkbenchView from './TextileWorkbenchView.vue'

beforeEach(() => {
  vi.clearAllMocks()
  mocks.authSnapshot.value = {
    status: 'authenticated',
    user: {
      access_token: 'token',
      profile: {
        capability: ['textile.sample-requirement.manage', 'textile.cutting-plan.approve']
      }
    }
  }
  mocks.calculate.mockResolvedValue(requirement('SUFFICIENT'))
  mocks.create.mockResolvedValue(plan('DRAFT'))
  mocks.approve.mockResolvedValue(plan('APPROVED'))
  mocks.get.mockResolvedValue(plan('APPROVED'))
})

describe('Textile workbench view', () => {
  it('calculates a version-zero initial requirement and rejects an unknown direction locally', async () => {
    const wrapper = mount(TextileWorkbenchView)
    await wrapper.findAll('form')[0]!.trigger('submit')
    await flushPromises()

    expect(mocks.calculate).toHaveBeenCalledWith(
      expect.objectContaining({
        requirementId: 'TEXTILE-REQ-1',
        expectedCurrentVersion: 0,
        calculation: expect.objectContaining({ ruleSetVersion: 'TEXTILE-SAMPLE-REQUIREMENT@1.0.0' })
      }),
      { accessToken: 'token' }
    )
    expect(wrapper.text()).toContain('SUFFICIENT')

    const textarea = wrapper.get('textarea')
    const invalid = JSON.parse(textarea.element.value)
    invalid.calculation.demandLines[0].direction = 'DIAGONAL'
    await textarea.setValue(JSON.stringify(invalid))
    await wrapper.findAll('form')[0]!.trigger('submit')

    expect(mocks.calculate).toHaveBeenCalledTimes(1)
    expect(wrapper.text()).toContain('批准方向')
  })

  it('creates, approves, and reloads a plan using exact server-owned versions and hashes', async () => {
    const wrapper = mount(TextileWorkbenchView)
    const write = wrapper.findAll('form')[0]!
    await write.get('select').setValue('plan')
    await flushPromises()
    await write.trigger('submit')
    await flushPromises()
    expect(mocks.create).toHaveBeenCalledWith(
      expect.objectContaining({ expectedCurrentVersion: 0, sampleRequirementVersion: 1 }),
      { accessToken: 'token' }
    )

    await write.get('select').setValue('approval')
    await flushPromises()
    await write.trigger('submit')
    await flushPromises()
    expect(mocks.approve).toHaveBeenCalledWith(
      'CUTTING-PLAN-1', 1,
      expect.objectContaining({
        expectedCurrentVersion: 1,
        sampleRequirementInputHash: 'server-requirement-input-hash'
      }),
      { accessToken: 'token' }
    )

    const lookup = wrapper.findAll('form')[1]!
    await lookup.trigger('submit')
    await flushPromises()
    expect(mocks.get).toHaveBeenCalledWith('CUTTING-PLAN-1', 1, { accessToken: 'token' })
    expect(wrapper.text()).toContain('APPROVED')
  })

  it('keeps UNKNOWN visibly blocked and separates manage from approve capability', async () => {
    mocks.calculate.mockResolvedValue(requirement('UNKNOWN'))
    mocks.authSnapshot.value = {
      status: 'authenticated',
      user: {
        access_token: 'token',
        profile: { capability: ['textile.sample-requirement.manage'] }
      }
    }
    const wrapper = mount(TextileWorkbenchView)
    await wrapper.findAll('form')[0]!.trigger('submit')
    await flushPromises()
    expect(wrapper.text()).toContain('UNKNOWN')

    await wrapper.findAll('form')[0]!.get('select').setValue('approval')
    await flushPromises()
    expect(wrapper.text()).toContain('当前身份没有 textile.cutting-plan.approve 能力')
    expect(wrapper.findAll('form')[0]!.get('button[type="submit"]').attributes('disabled')).toBeDefined()
  })

  it('preserves plan lookup and retries a network failure only after an explicit click', async () => {
    mocks.get
      .mockRejectedValueOnce(new LabApiError(
        'WEB.NETWORK_ERROR', 0, 'corr-textile', 'offline', 'retry explicitly'
      ))
      .mockResolvedValueOnce(plan('APPROVED'))
    const wrapper = mount(TextileWorkbenchView)
    const lookup = wrapper.findAll('form')[1]!
    await lookup.findAll('input')[0]!.setValue('CUTTING-PLAN-1')
    await lookup.findAll('input')[1]!.setValue('1')
    await lookup.trigger('submit')
    await flushPromises()
    expect(wrapper.text()).toContain('corr-textile')

    const retry = wrapper.findAll('button').find(button => button.text() === '显式重试')
    await retry!.trigger('click')
    await flushPromises()
    expect(mocks.get).toHaveBeenCalledTimes(2)
    expect(mocks.get.mock.calls[1]?.[0]).toBe('CUTTING-PLAN-1')
  })
})

function requirement(decision: 'SUFFICIENT' | 'UNKNOWN') {
  return {
    requirementId: 'TEXTILE-REQ-1', version: 1,
    objectScope: { legalEntityId: 'legal', laboratoryId: 'lab' },
    calculation: { ruleSetVersion: 'TEXTILE-SAMPLE-REQUIREMENT@1.0.0', demandLines: [], availableFabrics: [] },
    result: {
      decision, reasonCodes: decision === 'UNKNOWN' ? ['RULE_SET_VERSION_UNKNOWN'] : [],
      specimenPlans: [], gaps: [], ruleSetVersion: 'TEXTILE-SAMPLE-REQUIREMENT@1.0.0'
    },
    inputHash: 'server-requirement-input-hash', createdBy: 'creator', createdAt: '2026-08-05T00:00:00Z'
  }
}

function plan(state: 'DRAFT' | 'APPROVED') {
  return {
    cuttingPlanId: 'CUTTING-PLAN-1', version: 1,
    objectScope: { legalEntityId: 'legal', laboratoryId: 'lab' },
    sampleRequirement: requirement('SUFFICIENT'),
    plan: {
      cuttingPlanId: 'CUTTING-PLAN-1', sourceItem: { id: 'FABRIC', version: 1 },
      samplingPosition: 'BODY', direction: 'WARP', lengthMm: 10, widthMm: 12,
      plannedCount: 1, minDistanceFromSelvedgeMm: 20, templateVersion: 'TPL@1.0.0',
      operatorId: 'operator', generatedSpecimenIds: ['SPEC-1']
    },
    state, inputHash: 'plan-hash', ruleSetVersion: 'TEXTILE-SAMPLE-REQUIREMENT@1.0.0',
    createdBy: 'creator', createdAt: '2026-08-05T00:00:00Z',
    approval: state === 'APPROVED' ? {
      cuttingPlanId: 'CUTTING-PLAN-1', cuttingPlanVersion: 1,
      sampleRequirementId: 'TEXTILE-REQ-1', sampleRequirementVersion: 1,
      sampleRequirementInputHash: 'server-requirement-input-hash',
      ruleSetVersion: 'TEXTILE-SAMPLE-REQUIREMENT@1.0.0', approvedBy: 'approver',
      approvedAt: '2026-08-05T00:00:00Z'
    } : undefined
  }
}

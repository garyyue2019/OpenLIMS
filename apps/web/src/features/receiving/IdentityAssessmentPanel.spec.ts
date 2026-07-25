import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'

const { getIdentityAssessment, createIdentityObservation, submitIdentityDecision } = vi.hoisted(() => ({
  getIdentityAssessment: vi.fn(),
  createIdentityObservation: vi.fn(),
  submitIdentityDecision: vi.fn()
}))

vi.mock('../../auth-store', () => ({
  authSnapshot: {
    value: {
      status: 'authenticated',
      user: {
        profile: { capability: ['receiving.identity.evaluate'] },
        access_token: 'token-a'
      }
    }
  }
}))

vi.mock('./identity-client', async (importOriginal) => {
  const original = await importOriginal<typeof import('./identity-client')>()
  return { ...original, getIdentityAssessment, createIdentityObservation, submitIdentityDecision }
})

import IdentityAssessmentPanel from './IdentityAssessmentPanel.vue'

describe('identity assessment panel', () => {
  it('shows three evidence layers, highlights differences, submits bound versions, and keeps quarantine visible', async () => {
    getIdentityAssessment.mockResolvedValue(assessment())
    createIdentityObservation.mockResolvedValue({
      ...assessment(),
      itemVersion: 3,
      assessmentVersion: 2,
      observations: [
        ...assessment().observations,
        { ...assessment().observations[0], observationId: 'obs-2', version: 2, expectedItemVersion: 2 }
      ]
    })
    submitIdentityDecision.mockResolvedValue({
      ...assessment(),
      itemVersion: 4,
      assessmentState: 'MATCHED',
      assessmentVersion: 3,
      decisions: [{
        decisionId: 'decision-1', version: 1, observationVersion: 2, declarationSnapshotVersion: 1,
        outcome: 'MATCHED', reasonCode: 'CONSISTENT', rationale: 'Reviewed.',
        ruleSetVersion: 'REC-ELIGIBILITY@1.0.0', decidedAt: '2026-07-25T02:05:00Z', decidedBy: 'actor-a'
      }]
    })

    const wrapper = mount(IdentityAssessmentPanel, {
      props: { receivedItemId: 'item-a' },
      global: {
        stubs: {
          'a-alert': { props: ['message', 'description'], template: '<div class="alert">{{ message }} {{ description }}</div>' },
          'a-button': { template: '<button :disabled="$attrs.disabled"><slot /></button>' }
        }
      }
    })
    await flushPromises()

    expect(wrapper.text()).toContain('1. 客户声明快照')
    expect(wrapper.text()).toContain('2. 实验室观察')
    expect(wrapper.text()).toContain('3. 人工结论')
    expect(wrapper.text()).toContain('仍在隔离')
    expect(wrapper.text()).toContain('QUARANTINED')

    const observationForm = wrapper.find('form[aria-labelledby="observation-heading"]')
    const inputs = observationForm.findAll('input')
    await inputs[1].setValue('MODEL-OTHER')
    expect(observationForm.text()).toContain('与声明型号不一致')
    await observationForm.trigger('submit')
    await flushPromises()

    expect(createIdentityObservation).toHaveBeenCalledWith('item-a', expect.objectContaining({
      expectedItemVersion: 2,
      observedModel: 'MODEL-OTHER',
      attachmentHashes: ['a'.repeat(64)]
    }), 'token-a')

    const decisionForm = wrapper.find('form[aria-labelledby="decision-heading"]')
    await decisionForm.find('textarea').setValue('Reviewed all evidence.')
    await decisionForm.trigger('submit')
    await flushPromises()

    expect(submitIdentityDecision).toHaveBeenCalledWith('item-a', expect.objectContaining({
      expectedItemVersion: 3,
      observationVersion: 2,
      declarationSnapshotVersion: 1,
      ruleSetVersion: 'REC-ELIGIBILITY@1.0.0'
    }), 'token-a')
    expect(wrapper.text()).toContain('只读版本历史')
    expect(wrapper.text()).toContain('仍在隔离')
  })
})

function assessment() {
  return {
    receivedItemId: 'item-a',
    receivedItemNumber: 'ITM-A',
    currentState: 'QUARANTINED',
    itemVersion: 2,
    assessmentState: 'IN_PROGRESS',
    assessmentVersion: 1,
    declarationSnapshot: {
      receivedItemId: 'item-a', snapshotVersion: 1, itemVersion: 1,
      declaredDescription: 'Hard plastic toy set', model: 'MODEL-001', batch: 'BATCH-001',
      serialNumber: 'SERIAL-001', color: 'red', capturedAt: '2026-07-25T02:00:00Z'
    },
    observations: [{
      observationId: 'obs-1', version: 1, expectedItemVersion: 1,
      observedLabels: ['LABEL-01'], observedModel: 'MODEL-001', observedBatch: 'BATCH-001',
      appearance: 'intact red toy set', attachmentRefs: ['object://photo'], attachmentHashes: ['a'.repeat(64)],
      observedAt: '2026-07-25T02:01:00Z', observedBy: 'actor-a'
    }],
    decisions: []
  }
}

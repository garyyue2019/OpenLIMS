import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'

const { submitReceivingReleaseDecision } = vi.hoisted(() => ({
  submitReceivingReleaseDecision: vi.fn()
}))

vi.mock('../../auth-store', () => ({ authSnapshot: { value: {
  status: 'authenticated', user: {
    profile: { capability: ['receiving.release.approve'] },
    access_token: 'token-a'
  }
} } }))
vi.mock('./release-client', async (importOriginal) => ({
  ...await importOriginal<typeof import('./release-client')>(),
  submitReceivingReleaseDecision
}))

import ReceivingReleasePanel from './ReceivingReleasePanel.vue'

describe('receiving release panel', () => {
  it('submits the pinned rule and renders constrained eligibility returned by the server', async () => {
    submitReceivingReleaseDecision.mockResolvedValue({
      releaseDecisionId: 'release-a', version: 1, receivedItemId: 'item-a', receivedItemNumber: 'ITM-A',
      boundItemVersion: 5, itemVersion: 6, state: 'CONDITIONALLY_ACCEPTED',
      identityDecisionId: 'identity-a', identityDecisionVersion: 1,
      exceptionDecisionVersions: [{ exceptionId: 'exception-a', status: 'CONDITIONALLY_ACCEPTED',
        exceptionVersion: 2, decisionId: 'decision-a', decisionVersion: 1, matrixVersion: 'OD-005@1.0.0' }],
      releaseRuleVersion: 'REC-RELEASE@2.0.0', exceptionMatrixVersion: 'OD-005@1.0.0',
      outcome: 'RELEASED_WITH_CONSTRAINTS', allowedActions: ['DISASSEMBLY'],
      prohibitedActions: ['SAMPLE_PREPARATION'], constraintsValidUntil: '2026-08-01T10:00:00Z',
      rationale: 'Quality review complete.', approvedAt: '2026-07-26T10:00:00Z', approvedBy: 'quality-a'
    })
    const wrapper = mount(ReceivingReleasePanel, {
      props: { receivedItemId: 'item-a', itemVersion: 5, itemState: 'QUARANTINED' },
      global: { stubs: {
        'a-alert': { props: ['message', 'description'], template: '<div>{{ message }} {{ description }}</div>' },
        'a-button': { template: '<button :disabled="$attrs.disabled"><slot /></button>' }
      } }
    })

    await wrapper.find('textarea').setValue('Quality review complete.')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(submitReceivingReleaseDecision).toHaveBeenCalledWith('item-a', {
      expectedItemVersion: 5,
      ruleSetVersion: 'REC-RELEASE@2.0.0',
      rationale: 'Quality review complete.'
    }, 'token-a')
    expect(wrapper.emitted('itemVersionChanged')).toEqual([[6]])
    expect(wrapper.emitted('itemStateChanged')).toEqual([['CONDITIONALLY_ACCEPTED']])
    expect(wrapper.text()).toContain('RELEASED_WITH_CONSTRAINTS')
    expect(wrapper.text()).toContain('DISASSEMBLY')
    expect(wrapper.text()).toContain('SAMPLE_PREPARATION')
  })
})

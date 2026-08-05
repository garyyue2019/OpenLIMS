import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'

const { createReceivingException, getReceivingException, submitReceivingExceptionDecision } = vi.hoisted(() => ({
  createReceivingException: vi.fn(), getReceivingException: vi.fn(), submitReceivingExceptionDecision: vi.fn()
}))

vi.mock('../../auth-store', () => ({ authSnapshot: { value: {
  status: 'authenticated', user: {
    profile: { capability: ['exception.create', 'exception.read', 'exception.quality.approve'] },
    access_token: 'token-a'
  }
} } }))
vi.mock('./exception-client', async (importOriginal) => ({
  ...await importOriginal<typeof import('./exception-client')>(),
  createReceivingException, getReceivingException, submitReceivingExceptionDecision
}))

import ReceivingExceptionPanel from './ReceivingExceptionPanel.vue'

beforeEach(() => { vi.clearAllMocks() })

describe('receiving exception panel', () => {
  it('creates a fact, submits explicit conditional constraints, and keeps quarantine visible', async () => {
    createReceivingException.mockResolvedValue(exceptionResult())
    submitReceivingExceptionDecision.mockResolvedValue({
      ...exceptionResult(), itemVersion: 5, status: 'CONDITIONALLY_ACCEPTED', version: 2,
      decisions: [{ decisionId: 'decision-a', version: 1, decisionType: 'CONDITIONAL_ACCEPT',
        allowedActions: ['DISASSEMBLY'], prohibitedActions: ['SAMPLE_PREPARATION'],
        decidedAt: '2026-07-25T10:05:00Z', decidedBy: 'quality-a' }]
    })
    const wrapper = mount(ReceivingExceptionPanel, {
      props: { receivedItemId: 'item-a', itemVersion: 3 },
      global: { stubs: {
        'a-alert': { props: ['message', 'description'], template: '<div>{{ message }} {{ description }}</div>' },
        'a-button': { template: '<button :disabled="$attrs.disabled"><slot /></button>' }
      } }
    })
    expect(wrapper.text()).toContain('仍保持 QUARANTINED')
    const createForm = wrapper.find('form')
    await createForm.find('textarea').setValue('Quantity is below the approved need.')
    const createInputs = createForm.findAll('input')
    await createInputs[0].setValue('object://exception/evidence')
    await createInputs[1].setValue('a'.repeat(64))
    await createForm.trigger('submit'); await flushPromises()
    expect(createReceivingException).toHaveBeenCalledWith(expect.objectContaining({
      receivedItemId: 'item-a', expectedItemVersion: 3, type: 'QUANTITY_SHORTAGE'
    }), 'token-a')

    const decisionForm = wrapper.find('form')
    await decisionForm.find('select').setValue('CONDITIONAL_ACCEPT')
    const inputs = decisionForm.findAll('input')
    await inputs[0].setValue('DISASSEMBLY')
    await inputs[1].setValue('SAMPLE_PREPARATION')
    await inputs[2].setValue('2026-08-01T10:00')
    await inputs[3].setValue('Impact reviewed.')
    await inputs[4].setValue('object://exception/decision')
    await inputs[5].setValue('b'.repeat(64))
    await decisionForm.find('textarea').setValue('Explicit constraints approved.')
    await decisionForm.trigger('submit'); await flushPromises()
    expect(submitReceivingExceptionDecision).toHaveBeenCalledWith('exception-a', expect.objectContaining({
      expectedVersion: 1, decisionType: 'CONDITIONAL_ACCEPT',
      allowedActions: ['DISASSEMBLY'], prohibitedActions: ['SAMPLE_PREPARATION'],
      matrixVersion: 'OD-005@1.0.0'
    }), 'token-a')
    expect(wrapper.text()).toContain('CONDITIONALLY_ACCEPTED')
    expect(wrapper.text()).toContain('仍保持 QUARANTINED')
  })

  it('loads an existing exception by stable id and rejects a mismatched item binding', async () => {
    getReceivingException.mockResolvedValueOnce(exceptionResult())
    const wrapper = mount(ReceivingExceptionPanel, {
      props: { receivedItemId: 'item-a', itemVersion: 3, exceptionId: 'exception-a' },
      global: { stubs: {
        'a-alert': { props: ['message', 'description'], template: '<div>{{ message }} {{ description }}</div>' },
        'a-button': { template: '<button :disabled="$attrs.disabled"><slot /></button>' }
      } }
    })
    await flushPromises()
    expect(getReceivingException).toHaveBeenCalledWith('exception-a', 'token-a')
    expect(wrapper.text()).toContain('QUANTITY_SHORTAGE')
    expect(wrapper.emitted('itemVersionChanged')).toEqual([[4]])

    getReceivingException.mockResolvedValueOnce({ ...exceptionResult(), receivedItemId: 'item-b' })
    await wrapper.setProps({ exceptionId: 'exception-b' })
    await flushPromises()
    expect(wrapper.text()).toContain('EXCEPTION_OBJECT_MISMATCH')
    expect(wrapper.text()).not.toContain('item-b')
  })
})

function exceptionResult() {
  return {
    exceptionId: 'exception-a', receivedItemId: 'item-a', receivedItemNumber: 'ITM-A',
    itemVersion: 4, itemState: 'QUARANTINED', type: 'QUANTITY_SHORTAGE', severity: 'STANDARD',
    description: 'Quantity is below the approved need.', observedAt: '2026-07-25T10:00:00Z',
    evidenceRefs: ['object://exception/evidence'], evidenceHashes: ['a'.repeat(64)],
    createdBy: 'creator-a', createdAt: '2026-07-25T10:00:00Z', status: 'OPEN', version: 1, decisions: []
  }
}

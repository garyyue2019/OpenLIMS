import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'

const { resolveLabelScan } = vi.hoisted(() => ({
  resolveLabelScan: vi.fn().mockResolvedValue({
    objectType: 'RI',
    objectId: 'item-a',
    businessNumber: 'LAB-A-RI-20260724-000001',
    state: 'QUARANTINED',
    printVerificationStatus: 'VERIFIED',
    allowedActions: ['reprint']
  })
}))

vi.mock('../../auth-store', () => ({
  authSnapshot: {
    value: {
      status: 'authenticated',
      user: {
        profile: { capability: ['receiving.label.scan', 'receiving.label.print', 'receiving.label.reprint'] },
        access_token: 'token-a'
      }
    }
  }
}))

vi.mock('./labeling-client', async (importOriginal) => {
  const original = await importOriginal<typeof import('./labeling-client')>()
  return { ...original, resolveLabelScan }
})

import ReceivingRegistrationView from './ReceivingRegistrationView.vue'

describe('receiving label scan view', () => {
  it('accepts keyboard-wedge scanner input and shows verified quarantine state', async () => {
    const wrapper = mount(ReceivingRegistrationView, {
      global: {
        stubs: {
          'a-alert': { props: ['message'], template: '<div>{{ message }}</div>' },
          'a-button': { template: '<button :disabled="$attrs.disabled"><slot /></button>' }
        }
      }
    })
    const scanSection = wrapper.find('section[aria-labelledby="scan-heading"]')
    await scanSection.find('input').setValue('OL1:RI:opaque:checksum')
    await scanSection.find('form').trigger('submit')
    await flushPromises()

    expect(resolveLabelScan).toHaveBeenCalledWith('OL1:RI:opaque:checksum', 'token-a')
    expect(scanSection.text()).toContain('实物 · LAB-A-RI-20260724-000001')
    expect(scanSection.text()).toContain('QUARANTINED')
    expect(scanSection.text()).toContain('VERIFIED')
    expect(wrapper.html()).not.toContain('organizationGroupId')
  })
})

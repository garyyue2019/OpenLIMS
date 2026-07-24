import { mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'

vi.mock('../../auth-store', () => ({
  authSnapshot: {
    value: {
      status: 'authenticated',
      user: { profile: { capability: 'system_admin' }, access_token: 'token' }
    }
  }
}))

import ReceivingRegistrationView from './ReceivingRegistrationView.vue'

describe('receiving registration view', () => {
  it('shows separated package and complete-item structure in read-only mode without a group field', () => {
    const wrapper = mount(ReceivingRegistrationView, {
      global: {
        stubs: {
          'a-alert': {
            props: ['message', 'description'],
            template: '<div class="alert">{{ message }} {{ description }}</div>'
          },
          'a-button': { template: '<button><slot /></button>' }
        }
      }
    })

    expect(wrapper.text()).toContain('到货、包装与实物登记')
    expect(wrapper.text()).toContain('包装 1')
    expect(wrapper.text()).toContain('完整玩具或套装 1')
    expect(wrapper.text()).toContain('系统管理员默认不会自动获得')
    expect(wrapper.findAll('input').every(input => input.attributes('disabled') !== undefined)).toBe(true)
    expect(wrapper.html()).not.toContain('organizationGroupId')
    expect(wrapper.html()).not.toContain('解除隔离')
  })
})

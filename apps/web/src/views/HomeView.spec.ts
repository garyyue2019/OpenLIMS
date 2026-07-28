import { mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'

vi.mock('../auth-store', () => ({
  authSnapshot: { status: 'authenticated', user: { access_token: 'token' } },
  runtimeConfig: { environmentLabel: 'Test' },
  signIn: vi.fn()
}))
vi.mock('vue-router', () => ({
  useRoute: () => ({ fullPath: '/' }),
  RouterLink: {
    props: ['to'],
    template: '<a :data-route="to.name"><slot /></a>'
  }
}))

import HomeView from './HomeView.vue'

describe('operator home view', () => {
  it('links an authenticated operator to the complete receiving-through-batch flow', () => {
    const wrapper = mount(HomeView, {
      global: {
        stubs: {
          'a-card': { template: '<section><slot /></section>' },
          'a-descriptions': { template: '<dl><slot /></dl>' },
          'a-descriptions-item': { template: '<dd><slot /></dd>' },
          'a-alert': true,
          'a-spin': true,
          'a-button': true,
          'a-result': true
        }
      }
    })

    expect(wrapper.findAll('[data-route]').map(link => link.attributes('data-route'))).toEqual([
      'receiving.registration', 'workbench.scope', 'workbench.quantity',
      'workbench.allocation', 'workbench.batch'
    ])
  })
})

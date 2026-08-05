import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'

const mocks = vi.hoisted(() => ({
  route: { params: {} as Record<string, unknown>, query: {} as Record<string, unknown>, fullPath: '/receiving/items/continue' },
  replace: vi.fn(),
  authSnapshot: { value: { status: 'authenticated', user: { access_token: 'token', profile: {} } } as Record<string, unknown> }
}))

vi.mock('../../auth-store', () => ({ authSnapshot: mocks.authSnapshot }))
vi.mock('vue-router', () => ({ useRoute: () => mocks.route, useRouter: () => ({ replace: mocks.replace }) }))

import ReceivingContinuationView from './ReceivingContinuationView.vue'

const stubs = {
  'a-alert': { props: ['message', 'description'], template: '<div>{{ message }} {{ description }}</div>' },
  'a-button': { template: '<button :type="$attrs.htmlType" :disabled="$attrs.disabled"><slot /></button>' },
  IdentityAssessmentPanel: {
    props: ['receivedItemId'], emits: ['itemVersionChanged'],
    template: '<button class="identity-stub" @click="$emit(\'itemVersionChanged\', 6)">identity {{ receivedItemId }}</button>'
  },
  ReceivingExceptionPanel: {
    props: ['receivedItemId', 'itemVersion', 'exceptionId'], emits: ['itemVersionChanged'],
    template: '<div class="exception-stub">exception {{ receivedItemId }} v{{ itemVersion }} {{ exceptionId }}</div>'
  },
  ReceivingReleasePanel: {
    props: ['receivedItemId', 'itemVersion', 'itemState'], emits: ['itemVersionChanged', 'itemStateChanged'],
    template: '<button class="release-stub" @click="$emit(\'itemVersionChanged\', 7); $emit(\'itemStateChanged\', \'ACCEPTED\')">release {{ receivedItemId }} v{{ itemVersion }} {{ itemState }}</button>'
  }
}

beforeEach(() => {
  vi.clearAllMocks()
  mocks.route.params = {}
  mocks.route.query = {}
  mocks.route.fullPath = '/receiving/items/continue'
  mocks.authSnapshot.value = { status: 'authenticated', user: { access_token: 'token', profile: {} } }
})

describe('receiving continuation view', () => {
  it('restores a stable deep link and propagates server-returned version and state', async () => {
    mocks.route.params = { receivedItemId: 'item-a' }
    mocks.route.query = { itemVersion: '5', itemState: 'QUARANTINED', exceptionId: 'exception-a' }
    mocks.route.fullPath = '/receiving/items/item-a?itemVersion=5&itemState=QUARANTINED&exceptionId=exception-a'
    const wrapper = mount(ReceivingContinuationView, { global: { stubs } })
    await flushPromises()

    expect(wrapper.text()).toContain('item-a')
    expect(wrapper.text()).toContain('v5')
    expect(wrapper.text()).toContain('exception-a')
    expect(wrapper.get('.exception-stub').text()).toContain('v5')

    await wrapper.get('.identity-stub').trigger('click')
    await flushPromises()
    expect(wrapper.text()).toContain('v6')
    expect(mocks.replace).toHaveBeenLastCalledWith(expect.objectContaining({
      name: 'receiving.item-continuation', params: { receivedItemId: 'item-a' },
      query: expect.objectContaining({ itemVersion: '6', itemState: 'QUARANTINED', exceptionId: 'exception-a' })
    }))

    await wrapper.get('.release-stub').trigger('click')
    await flushPromises()
    expect(wrapper.text()).toContain('v7')
    expect(wrapper.text()).toContain('ACCEPTED')
  })

  it('opens explicit input as a stable route without registering a new receipt', async () => {
    const wrapper = mount(ReceivingContinuationView, { global: { stubs } })
    const form = wrapper.get('form')
    const inputs = form.findAll('input')
    await inputs[0]!.setValue('item/1')
    await inputs[1]!.setValue('3')
    await form.get('select').setValue('CONDITIONALLY_ACCEPTED')
    await inputs[2]!.setValue('exception/1')
    await form.trigger('submit')
    await flushPromises()

    expect(wrapper.text()).toContain('item/1')
    expect(mocks.replace).toHaveBeenCalledWith({
      name: 'receiving.item-continuation', params: { receivedItemId: 'item/1' },
      query: { itemVersion: '3', itemState: 'CONDITIONALLY_ACCEPTED', exceptionId: 'exception/1' }
    })
    expect(wrapper.html()).not.toContain('到货时间')
  })

  it('fails closed for an incomplete deep link or invalid explicit version', async () => {
    mocks.route.params = { receivedItemId: 'item-a' }
    mocks.route.query = { itemVersion: '0', itemState: 'UNKNOWN' }
    const wrapper = mount(ReceivingContinuationView, { global: { stubs } })
    await flushPromises()
    expect(wrapper.text()).toContain('深链接缺少正整数')
    expect(wrapper.find('.identity-stub').exists()).toBe(false)

    const inputs = wrapper.get('form').findAll('input')
    await inputs[0]!.setValue('item-a')
    await inputs[1]!.setValue('0')
    await wrapper.get('form').trigger('submit')
    expect(wrapper.text()).toContain('正整数 itemVersion')
    expect(mocks.replace).not.toHaveBeenCalled()
  })
})

import ReceivingRegistrationView from './ReceivingRegistrationView.vue'
import ReceivingContinuationView from './ReceivingContinuationView.vue'
import type { WebFeatureDescriptor } from '../../web-feature'

export const receivingFeature = {
  featureId: 'RECEIVING-REGISTRATION',
  contractVersion: '1.0.0',
  routes: [
    {
      name: 'receiving.registration',
      path: '/receiving/receipts/new',
      component: ReceivingRegistrationView
    },
    {
      name: 'receiving.continuation',
      path: '/receiving/items/continue',
      component: ReceivingContinuationView
    },
    {
      name: 'receiving.item-continuation',
      path: '/receiving/items/:receivedItemId',
      component: ReceivingContinuationView
    }
  ],
  navigationEntries: [
    {
      id: 'receiving.registration',
      label: '到货登记',
      routeName: 'receiving.registration'
    },
    {
      id: 'receiving.continuation',
      label: '既有实物续办',
      routeName: 'receiving.continuation'
    }
  ]
} as const satisfies WebFeatureDescriptor

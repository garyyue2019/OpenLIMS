import ReceivingRegistrationView from './ReceivingRegistrationView.vue'
import type { WebFeatureDescriptor } from '../../web-feature'

export const receivingFeature = {
  featureId: 'RECEIVING-REGISTRATION',
  contractVersion: '1.0.0',
  routes: [
    {
      name: 'receiving.registration',
      path: '/receiving/receipts/new',
      component: ReceivingRegistrationView
    }
  ],
  navigationEntries: [
    {
      id: 'receiving.registration',
      label: '到货登记',
      routeName: 'receiving.registration'
    }
  ]
} as const satisfies WebFeatureDescriptor

import { describe, expect, it } from 'vitest'
import { receivingFeature } from './receiving-feature'

describe('Receiving feature', () => {
  it('keeps registration and adds stable continuation routes', () => {
    expect(receivingFeature.routes.map(route => [route.name, route.path])).toEqual([
      ['receiving.registration', '/receiving/receipts/new'],
      ['receiving.continuation', '/receiving/items/continue'],
      ['receiving.item-continuation', '/receiving/items/:receivedItemId']
    ])
    expect(receivingFeature.navigationEntries).toEqual([
      { id: 'receiving.registration', label: '到货登记', routeName: 'receiving.registration' },
      { id: 'receiving.continuation', label: '既有实物续办', routeName: 'receiving.continuation' }
    ])
  })
})

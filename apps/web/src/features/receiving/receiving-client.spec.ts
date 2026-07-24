import { describe, expect, it, vi } from 'vitest'
import {
  ReceiptApiError,
  registerReceipt,
  type RegisterReceiptRequest,
  type ReceiptRegistrationResult
} from './receiving-client'

const request: RegisterReceiptRequest = {
  legalEntityId: 'legal-a',
  laboratoryId: 'lab-a',
  customerId: 'customer-a',
  serviceOrderId: 'order-a',
  arrivalAt: '2026-07-24T00:55:00.000Z',
  containers: [{
    externalLabel: 'BOX-01',
    packageType: 'carton',
    condition: 'intact',
    sealObservation: 'sealed',
    receivedItems: [{
      declaredDescription: 'Hard plastic toy set',
      model: 'MODEL-001',
      batch: 'BATCH-001',
      serialNumber: 'SERIAL-001',
      color: 'red',
      packageCondition: 'intact',
      sealCondition: 'sealed',
      itemCondition: 'intact',
      quantity: 1,
      unit: 'set'
    }]
  }]
}

const result: ReceiptRegistrationResult = {
  receiptId: 'receipt-id',
  receiptNumber: 'RCP-001',
  aggregateVersion: 1,
  containers: [{
    containerId: 'container-id',
    containerNumber: 'CNT-001',
    receivedItems: [{
      receivedItemId: 'item-id',
      receivedItemNumber: 'ITM-001',
      state: 'QUARANTINED',
      version: 1
    }]
  }]
}

describe('receiving client', () => {
  it('sends the exact authenticated idempotent contract without client group context', async () => {
    const fetcher = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      void input
      void init
      return response(true, 201, result)
    })

    const actual = await registerReceipt(request, 'access-token', 'idem-001', fetcher as typeof fetch)

    expect(actual).toEqual(result)
    expect(fetcher).toHaveBeenCalledOnce()
    const [url, init] = fetcher.mock.calls[0]
    expect(url).toBe('/api/v1/receipts')
    expect(init?.method).toBe('POST')
    expect(init?.headers).toEqual({
      Authorization: 'Bearer access-token',
      'Content-Type': 'application/json',
      'Idempotency-Key': 'idem-001'
    })
    expect(init?.body).not.toContain('organizationGroupId')
    expect(JSON.parse(init?.body as string)).toEqual(request)
  })

  it('preserves stable server error codes without leaking response fields', async () => {
    const fetcher = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      void input
      void init
      return response(false, 409, {
        errorCode: 'REC.IDEMPOTENCY_CONFLICT',
        correlationId: 'corr-001',
        internalDetail: 'must-not-be-used'
      })
    })

    const error = await registerReceipt(request, 'access-token', 'idem-001', fetcher as typeof fetch)
      .catch(value => value)

    expect(error).toBeInstanceOf(ReceiptApiError)
    expect(error).toMatchObject({ errorCode: 'REC.IDEMPOTENCY_CONFLICT', status: 409 })
    expect(error.message).not.toContain('must-not-be-used')
  })
})

function response(ok: boolean, status: number, payload: object): Response {
  return {
    ok,
    status,
    json: async () => payload
  } as Response
}

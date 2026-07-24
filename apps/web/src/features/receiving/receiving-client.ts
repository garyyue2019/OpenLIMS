export interface RegisterReceivedItemRequest {
  declaredDescription: string
  model: string
  batch: string
  serialNumber?: string
  color: string
  packageCondition: string
  sealCondition: string
  itemCondition: string
  quantity: 1
  unit: string
}

export interface RegisterContainerRequest {
  externalLabel?: string
  packageType: string
  condition: string
  sealObservation?: string
  receivedItems: RegisterReceivedItemRequest[]
}

export interface RegisterReceiptRequest {
  legalEntityId: string
  laboratoryId: string
  customerId: string
  serviceOrderId: string
  arrivalAt: string
  containers: RegisterContainerRequest[]
}

export interface ReceivedItemRegistrationResult {
  receivedItemId: string
  receivedItemNumber: string
  state: 'QUARANTINED'
  version: number
}

export interface ContainerRegistrationResult {
  containerId: string
  containerNumber: string
  receivedItems: ReceivedItemRegistrationResult[]
}

export interface ReceiptRegistrationResult {
  receiptId: string
  receiptNumber: string
  aggregateVersion: number
  containers: ContainerRegistrationResult[]
}

export class ReceiptApiError extends Error {
  constructor(
    public readonly errorCode: string,
    public readonly status: number
  ) {
    super(errorCode)
    this.name = 'ReceiptApiError'
  }
}

export async function registerReceipt(
  request: RegisterReceiptRequest,
  accessToken: string,
  idempotencyKey: string,
  fetcher: typeof fetch = fetch
): Promise<ReceiptRegistrationResult> {
  const response = await fetcher('/api/v1/receipts', {
    method: 'POST',
    headers: {
      Authorization: `Bearer ${accessToken}`,
      'Content-Type': 'application/json',
      'Idempotency-Key': idempotencyKey
    },
    body: JSON.stringify(request)
  })

  const payload = await response.json() as Record<string, unknown>
  if (!response.ok) {
    const errorCode = typeof payload.errorCode === 'string'
      ? payload.errorCode
      : 'REC.UNEXPECTED_RESPONSE'
    throw new ReceiptApiError(errorCode, response.status)
  }

  return payload as unknown as ReceiptRegistrationResult
}

export function createIdempotencyKey(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    return crypto.randomUUID()
  }
  throw new Error('Secure random UUID support is required.')
}

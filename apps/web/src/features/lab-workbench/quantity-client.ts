import { labRequest, type LabClientContext } from './lab-api'

export const QUANTITY_RULE_SET_VERSION = 'SAMPLE-QUANTITY@1.0.0'

export interface QuantityObjectContext {
  legalEntityId: string
  laboratoryId: string
  customerId: string
  serviceOrderId: string
  productCategory: string
}

export interface QuantitySubjectReference {
  subjectType: 'RECEIVED_ITEM' | 'DERIVED_SAMPLE' | 'TEST_SPECIMEN'
  id: string
  version: number
}

export interface CreateQuantityAccountRequest {
  ruleSetVersion: typeof QUANTITY_RULE_SET_VERSION
  objectScope: QuantityObjectContext
  subject: QuantitySubjectReference
  subjectQuantifiable: boolean
  dimension: 'COUNT' | 'MASS' | 'LENGTH' | 'AREA' | 'VOLUME'
  unit: string
  precisionScale: number
  conservationTolerance: number
}

export interface PostQuantityEntryRequest {
  expectedCurrentVersion: number
  ruleSetVersion: typeof QUANTITY_RULE_SET_VERSION
  entryType: 'RECEIPT' | 'OUTPUT' | 'RESERVE' | 'RESERVE_RELEASE' | 'ALLOCATE' | 'CONSUME' | 'RETURN' | 'LOSS' | 'DISPOSE' | 'REVERSAL' | 'RESTATE'
  amount: number
  reason?: string
  referencedEntryId?: string
  reservationId?: string
}

export interface QuantityAccountResult {
  quantityAccountId: string
  version: number
  ruleSetVersion: string
  objectScope: QuantityObjectContext
  subject: QuantitySubjectReference
  dimension: CreateQuantityAccountRequest['dimension']
  unit: string
  precisionScale: number
  conservationTolerance: number
  balance: number
  reserved: number
  available: number
  createdBy: string
  createdAt: string
}

export interface QuantityEntryResult {
  entryId: string
  quantityAccountId: string
  accountVersion: number
  entryType: PostQuantityEntryRequest['entryType']
  amount: number
  resultingBalance: number
  resultingReserved: number
  resultingAvailable: number
  referencedEntryId?: string
  reservationId?: string
  reason?: string
  postedBy: string
  postedAt: string
}

export interface QuantityAvailabilityResult {
  decision: 'ALLOWED' | 'BLOCKED' | 'UNKNOWN'
  reasonCodes: string[]
  quantityAccountId?: string
  currentAccountVersion?: number
  availableAmount?: number
  ruleSetVersion: string
}

export function createQuantityAccount(
  request: CreateQuantityAccountRequest,
  context: LabClientContext
): Promise<QuantityAccountResult> {
  return labRequest('/api/v1/quantity-accounts', { ...context, method: 'POST', body: request })
}

export function postQuantityEntry(
  accountId: string,
  request: PostQuantityEntryRequest,
  context: LabClientContext
): Promise<QuantityEntryResult> {
  return labRequest(`/api/v1/quantity-accounts/${encodeURIComponent(accountId)}/entries`, {
    ...context, method: 'POST', body: request
  })
}

export function getQuantityAccount(
  accountId: string,
  context: LabClientContext
): Promise<QuantityAccountResult> {
  return labRequest(`/api/v1/quantity-accounts/${encodeURIComponent(accountId)}`, context)
}

export function getQuantityAvailability(
  accountId: string,
  expectedVersion: number,
  requestedAmount: number,
  context: LabClientContext
): Promise<QuantityAvailabilityResult> {
  const query = new URLSearchParams({
    expectedVersion: String(expectedVersion),
    requestedAmount: String(requestedAmount),
    ruleSetVersion: QUANTITY_RULE_SET_VERSION
  })
  return labRequest(
    `/api/v1/quantity-accounts/${encodeURIComponent(accountId)}/availability?${query}`,
    context
  )
}

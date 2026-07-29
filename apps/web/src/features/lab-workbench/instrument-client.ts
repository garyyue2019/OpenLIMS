import { labRequest, type LabClientContext } from './lab-api'

export const INSTRUMENT_RULE_SET_VERSION = 'INST-IMPORT@1.0.0'

export interface InstrumentVersionedReference { id: string; version: number }
export interface InstrumentObjectContext { legalEntityId: string; laboratoryId: string }
export interface InstrumentRowMapping {
  sampleNumber: string
  batchPosition: string
  parameter: string
  unit: string
  qualifier?: string
}
export interface RegisterInstrumentFileRequest {
  ruleSetVersion: typeof INSTRUMENT_RULE_SET_VERSION
  objectScope: InstrumentObjectContext
  externalRef: InstrumentVersionedReference
  sha256: string
  sourceSystem: 'INSTRUMENT' | 'CDS' | 'MIDDLEWARE'
  instrumentRef: InstrumentVersionedReference
  parserVersion: string
  declaredRowCount: number
}
export interface InstrumentRowInput extends InstrumentRowMapping {
  rowNumber: number
  rawValue: string
  parsedValue: string
}
export interface SubmitInstrumentRowsRequest {
  expectedCurrentVersion: number
  ruleSetVersion: typeof INSTRUMENT_RULE_SET_VERSION
  rows: InstrumentRowInput[]
}
export interface ResolveImportExceptionRequest {
  expectedCurrentVersion: number
  ruleSetVersion: typeof INSTRUMENT_RULE_SET_VERSION
  kind: 'ACCEPT_WITH_MAPPING' | 'REJECT_ROW'
  reason: string
  correctedMapping?: InstrumentRowMapping
}
export interface InstrumentParsedRowResult extends InstrumentRowInput {
  rowId: string
  fileRegistrationId: string
  parserVersion: string
  recordedBy: string
  recordedAt: string
}
export interface InstrumentExceptionResolutionResult {
  resolutionId: string
  exceptionId: string
  kind: ResolveImportExceptionRequest['kind']
  correctedMapping?: InstrumentRowMapping
  reason: string
  resolvedBy: string
  resolvedAt: string
}
export interface InstrumentImportExceptionResult {
  exceptionId: string
  fileRegistrationId: string
  rowNumber: number
  reasonCode: string
  rawContent: string
  state: 'PENDING' | 'RESOLVED'
  resolution?: InstrumentExceptionResolutionResult
}
export interface InstrumentFileResult extends RegisterInstrumentFileRequest {
  fileRegistrationId: string
  version: number
  state: 'INGESTED' | 'BLOCKED' | 'COMPLETED'
  rows: InstrumentParsedRowResult[]
  exceptions: InstrumentImportExceptionResult[]
  registeredBy: string
  registeredAt: string
}
export interface InstrumentImportStatusResult {
  decision: 'ALLOWED' | 'BLOCKED' | 'UNKNOWN'
  reasonCodes: string[]
  fileRegistrationId: string
  currentVersion?: number
  completedRowCount?: number
  pendingExceptionCount?: number
  ruleSetVersion: string
}

export function registerInstrumentFile(
  request: RegisterInstrumentFileRequest,
  context: LabClientContext
): Promise<InstrumentFileResult> {
  return labRequest('/api/v1/instrument-files', { ...context, method: 'POST', body: request })
}

export function submitInstrumentRows(
  fileId: string,
  request: SubmitInstrumentRowsRequest,
  context: LabClientContext
): Promise<InstrumentFileResult> {
  return postInstrument(fileId, 'rows', request, context)
}

export function resolveInstrumentImportException(
  fileId: string,
  exceptionId: string,
  request: ResolveImportExceptionRequest,
  context: LabClientContext
): Promise<InstrumentFileResult> {
  const file = encodeURIComponent(fileId)
  const exception = encodeURIComponent(exceptionId)
  return labRequest(`/api/v1/instrument-files/${file}/exceptions/${exception}/resolution`, {
    ...context, method: 'POST', body: request
  })
}

export function getInstrumentFile(
  fileId: string,
  context: LabClientContext
): Promise<InstrumentFileResult> {
  return labRequest(`/api/v1/instrument-files/${encodeURIComponent(fileId)}`, context)
}

export function getInstrumentImportStatus(
  fileId: string,
  expectedFileVersion: number,
  context: LabClientContext
): Promise<InstrumentImportStatusResult> {
  const query = new URLSearchParams({
    expectedFileVersion: String(expectedFileVersion),
    ruleSetVersion: INSTRUMENT_RULE_SET_VERSION
  })
  return labRequest(
    `/api/v1/instrument-files/${encodeURIComponent(fileId)}/import-status?${query}`,
    context
  )
}

function postInstrument<T>(
  fileId: string,
  action: 'rows',
  body: unknown,
  context: LabClientContext
): Promise<T> {
  return labRequest(`/api/v1/instrument-files/${encodeURIComponent(fileId)}/${action}`, {
    ...context, method: 'POST', body
  })
}

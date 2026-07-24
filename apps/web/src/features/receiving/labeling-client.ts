export type LabelObjectType = 'CT' | 'RI'
export type LabelPrintStatus = 'REQUESTED' | 'DISPATCHING' | 'DISPATCHED' | 'VERIFIED' | 'FAILED' | 'UNKNOWN'

export interface LabelPrintTarget {
  objectType: LabelObjectType
  objectId: string
  objectVersion: number
}

export interface CreateLabelJobsRequest {
  printerId: string
  targets: LabelPrintTarget[]
}

export interface LabelPrintJobResult {
  printJobId: string
  objectType: LabelObjectType
  objectId: string
  businessNumber: string
  templateVersion: string
  printerId: string
  status: LabelPrintStatus
  isReprint: boolean
  successfulReprintCount: number
  createdAt: string
  updatedAt: string
}

export interface CreateLabelJobsResult {
  jobs: LabelPrintJobResult[]
}

export interface LabelScanResolution {
  objectType: LabelObjectType
  objectId: string
  businessNumber: string
  state: string
  printVerificationStatus: LabelPrintStatus | 'NOT_PRINTED'
  allowedActions: string[]
}

export class LabelingApiError extends Error {
  constructor(
    public readonly errorCode: string,
    public readonly status: number
  ) {
    super(errorCode)
    this.name = 'LabelingApiError'
  }
}

async function send<T>(
  path: string,
  accessToken: string,
  init: RequestInit,
  fetcher: typeof fetch
): Promise<T> {
  const response = await fetcher(path, {
    ...init,
    headers: {
      Authorization: `Bearer ${accessToken}`,
      'Content-Type': 'application/json',
      ...(init.headers ?? {})
    }
  })
  const payload = await response.json() as Record<string, unknown>
  if (!response.ok) {
    throw new LabelingApiError(
      typeof payload.errorCode === 'string' ? payload.errorCode : 'LABEL.UNEXPECTED_RESPONSE',
      response.status
    )
  }
  return payload as T
}

export function createLabelJobs(
  request: CreateLabelJobsRequest,
  accessToken: string,
  idempotencyKey: string,
  fetcher: typeof fetch = fetch
): Promise<CreateLabelJobsResult> {
  return send('/api/v1/label-jobs', accessToken, {
    method: 'POST',
    headers: { 'Idempotency-Key': idempotencyKey },
    body: JSON.stringify(request)
  }, fetcher)
}

export function getLabelJob(
  printJobId: string,
  accessToken: string,
  fetcher: typeof fetch = fetch
): Promise<LabelPrintJobResult> {
  return send(`/api/v1/label-jobs/${encodeURIComponent(printJobId)}`, accessToken, {
    method: 'GET'
  }, fetcher)
}

export function reprintLabel(
  printJobId: string,
  printerId: string,
  reason: string,
  accessToken: string,
  idempotencyKey: string,
  fetcher: typeof fetch = fetch
): Promise<CreateLabelJobsResult> {
  return send(`/api/v1/label-jobs/${encodeURIComponent(printJobId)}/reprint`, accessToken, {
    method: 'POST',
    headers: { 'Idempotency-Key': idempotencyKey },
    body: JSON.stringify({ printerId, reason })
  }, fetcher)
}

export function resolveLabelScan(
  barcodePayload: string,
  accessToken: string,
  fetcher: typeof fetch = fetch
): Promise<LabelScanResolution> {
  return send('/api/v1/scans/resolve', accessToken, {
    method: 'POST',
    body: JSON.stringify({ barcodePayload })
  }, fetcher)
}

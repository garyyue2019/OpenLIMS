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
    public readonly status: number,
    public readonly correlationId = 'not-available',
    public readonly detail?: string,
    public readonly nextAction?: string,
    public readonly title?: string
  ) {
    super(detail || title || errorCode)
    this.name = 'LabelingApiError'
  }

  get retryable(): boolean {
    return this.status === 0 || this.status >= 500
  }
}

async function send<T>(
  path: string,
  accessToken: string,
  init: RequestInit,
  fetcher: typeof fetch
): Promise<T> {
  const correlationId = createCorrelationId()
  let response: Response
  try {
    response = await fetcher(path, {
      ...init,
      headers: {
        Accept: 'application/json',
        Authorization: `Bearer ${accessToken}`,
        'Content-Type': 'application/json',
        'X-Correlation-Id': correlationId,
        ...(init.headers ?? {})
      }
    })
  } catch {
    throw new LabelingApiError(
      'WEB.NETWORK_ERROR',
      0,
      correlationId,
      'The labeling service could not be reached.',
      'Check the network connection and retry explicitly.'
    )
  }
  const payload = await readJson(response)
  if (!response.ok) {
    throw new LabelingApiError(
      stringValue(payload.errorCode) ?? fallbackErrorCode(response.status),
      response.status,
      stringValue(payload.correlationId) ?? response.headers.get('X-Correlation-Id') ?? correlationId,
      stringValue(payload.detail),
      stringValue(payload.nextAction),
      stringValue(payload.title)
    )
  }
  return payload as T
}

function createCorrelationId(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    return crypto.randomUUID()
  }
  throw new Error('Secure random UUID support is required.')
}

async function readJson(response: Response): Promise<Record<string, unknown>> {
  try {
    const payload = await response.json()
    return payload && typeof payload === 'object' && !Array.isArray(payload)
      ? payload as Record<string, unknown>
      : {}
  } catch {
    return {}
  }
}

function stringValue(value: unknown): string | undefined {
  return typeof value === 'string' && value.trim() ? value : undefined
}

function fallbackErrorCode(status: number): string {
  if (status === 401) return 'WEB.AUTH_REQUIRED'
  if (status === 403) return 'WEB.FORBIDDEN'
  if (status === 404) return 'WEB.OBJECT_NOT_ACCESSIBLE'
  return 'LABEL.UNEXPECTED_RESPONSE'
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

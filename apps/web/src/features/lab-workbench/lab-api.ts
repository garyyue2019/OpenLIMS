export interface LabProblemDetails {
  readonly title?: string
  readonly detail?: string
  readonly status?: number
  readonly errorCode?: string
  readonly correlationId?: string
  readonly nextAction?: string
  readonly gateSource?: string
}

export interface LabRequestOptions {
  readonly method?: 'GET' | 'POST'
  readonly accessToken: string
  readonly body?: unknown
  readonly correlationId?: string
  readonly signal?: AbortSignal
  readonly fetcher?: typeof fetch
}

export type LabClientContext = Pick<
  LabRequestOptions,
  'accessToken' | 'correlationId' | 'fetcher'
>

export class LabApiError extends Error {
  constructor(
    public readonly errorCode: string,
    public readonly status: number,
    public readonly correlationId: string,
    public readonly detail?: string,
    public readonly nextAction?: string,
    public readonly title?: string,
    public readonly gateSource?: string
  ) {
    super(detail || title || errorCode)
    this.name = 'LabApiError'
  }

  get retryable(): boolean {
    return this.status === 0 || this.status >= 500
  }
}

export async function labRequest<T>(
  path: string,
  options: LabRequestOptions
): Promise<T> {
  const correlationId = options.correlationId ?? createCorrelationId()
  if (!options.accessToken.trim()) {
    throw new LabApiError(
      'WEB.AUTH_REQUIRED',
      401,
      correlationId,
      'An authenticated session is required.'
    )
  }

  const headers: Record<string, string> = {
    Accept: 'application/json',
    Authorization: `Bearer ${options.accessToken}`,
    'X-Correlation-Id': correlationId
  }
  if (options.body !== undefined) headers['Content-Type'] = 'application/json'

  const request: RequestInit = {
    method: options.method ?? 'GET',
    headers
  }
  if (options.body !== undefined) request.body = JSON.stringify(options.body)
  if (options.signal) request.signal = options.signal

  let response: Response
  try {
    response = await (options.fetcher ?? fetch)(path, request)
  } catch {
    throw new LabApiError(
      'WEB.NETWORK_ERROR',
      0,
      correlationId,
      'The service could not be reached.',
      'Check the network connection and retry explicitly.'
    )
  }

  const payload = await readJson(response)
  if (!response.ok) {
    const problem = asProblemDetails(payload)
    throw new LabApiError(
      problem.errorCode ?? fallbackErrorCode(response.status),
      response.status,
      problem.correlationId ?? correlationId,
      problem.detail,
      problem.nextAction,
      problem.title,
      problem.gateSource
    )
  }

  return payload as T
}

export function createCorrelationId(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    return crypto.randomUUID()
  }
  throw new Error('Secure random UUID support is required.')
}

function asProblemDetails(payload: unknown): LabProblemDetails {
  if (!payload || typeof payload !== 'object') return {}
  const source = payload as Record<string, unknown>
  return {
    title: stringValue(source.title),
    detail: stringValue(source.detail),
    status: numberValue(source.status),
    errorCode: stringValue(source.errorCode),
    correlationId: stringValue(source.correlationId),
    nextAction: stringValue(source.nextAction),
    gateSource: stringValue(source.gateSource)
  }
}

async function readJson(response: Response): Promise<unknown> {
  if (response.status === 204) return undefined
  try {
    return await response.json()
  } catch {
    return undefined
  }
}

function stringValue(value: unknown): string | undefined {
  return typeof value === 'string' && value.trim() ? value : undefined
}

function numberValue(value: unknown): number | undefined {
  return typeof value === 'number' ? value : undefined
}

function fallbackErrorCode(status: number): string {
  if (status === 401) return 'WEB.AUTH_REQUIRED'
  if (status === 403) return 'WEB.FORBIDDEN'
  if (status === 404) return 'WEB.OBJECT_NOT_ACCESSIBLE'
  return 'WEB.UNEXPECTED_RESPONSE'
}

import { describe, expect, it, vi } from 'vitest'
import { LabApiError, labRequest } from './lab-api'

describe('labRequest', () => {
  it('sends authenticated JSON with a correlation id and returns the typed payload', async () => {
    const fetcher = vi.fn(async () => new Response(JSON.stringify({ version: 2 }), {
      status: 201,
      headers: { 'Content-Type': 'application/json' }
    })) as unknown as typeof fetch

    const result = await labRequest<{ version: number }>('/api/v1/example', {
      method: 'POST',
      accessToken: 'token-1',
      body: { expectedCurrentVersion: 1 },
      correlationId: 'corr-1',
      fetcher
    })

    expect(result).toEqual({ version: 2 })
    expect(fetcher).toHaveBeenCalledOnce()
    expect(fetcher).toHaveBeenCalledWith('/api/v1/example', {
      method: 'POST',
      headers: {
        Accept: 'application/json',
        Authorization: 'Bearer token-1',
        'Content-Type': 'application/json',
        'X-Correlation-Id': 'corr-1'
      },
      body: JSON.stringify({ expectedCurrentVersion: 1 })
    })
  })

  it('preserves RFC 9457 error code, correlation id, next action, and status', async () => {
    const fetcher = vi.fn(async () => new Response(JSON.stringify({
      title: 'Allocation blocked',
      detail: 'Quantity is insufficient.',
      status: 422,
      errorCode: 'ALC.ELIGIBILITY_BLOCKED',
      correlationId: 'corr-server',
      nextAction: 'Refresh quantity availability.'
    }), {
      status: 422,
      headers: { 'Content-Type': 'application/problem+json' }
    })) as unknown as typeof fetch

    await expect(labRequest('/api/v1/test-object-allocations', {
      method: 'POST', accessToken: 'token-1', body: {}, correlationId: 'corr-client', fetcher
    })).rejects.toMatchObject({
      name: 'LabApiError',
      errorCode: 'ALC.ELIGIBILITY_BLOCKED',
      correlationId: 'corr-server',
      nextAction: 'Refresh quantity availability.',
      status: 422,
      detail: 'Quantity is insufficient.'
    })
  })

  it('maps a network failure without retrying a write request', async () => {
    const fetcher = vi.fn(async () => { throw new TypeError('offline') }) as unknown as typeof fetch

    await expect(labRequest('/api/v1/batches', {
      method: 'POST', accessToken: 'token-1', body: {}, correlationId: 'corr-2', fetcher
    })).rejects.toEqual(expect.objectContaining<Partial<LabApiError>>({
      errorCode: 'WEB.NETWORK_ERROR',
      correlationId: 'corr-2',
      status: 0
    }))
    expect(fetcher).toHaveBeenCalledOnce()
  })

  it('fails closed when no access token is available', async () => {
    const fetcher = vi.fn() as unknown as typeof fetch

    await expect(labRequest('/api/v1/scope-matrices', {
      accessToken: '', correlationId: 'corr-3', fetcher
    })).rejects.toMatchObject({ errorCode: 'WEB.AUTH_REQUIRED', status: 401 })
    expect(fetcher).not.toHaveBeenCalled()
  })
})

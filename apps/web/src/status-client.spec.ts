import { describe, expect, it } from 'vitest'
import { getSystemStatus } from './status-client'
const config = { apiBaseUrl: '/api', environmentLabel: 'Verification', oidc: { authority: 'https://identity.example.test', clientId: 'openlims-web', scope: 'openid profile' } }
describe('getSystemStatus', () => {
  it('returns ready with a correlation identifier', async () => {
    const response = new Response(null, { status: 200, headers: { 'X-Correlation-Id': 'c-123' } })
    await expect(getSystemStatus(config, undefined, async () => response)).resolves.toEqual({ health: 'ready', correlationId: 'c-123' })
  })
  it('reports a 401 without losing safe diagnostics', async () => {
    const response = new Response(JSON.stringify({ errorCode: 'AUTH.SESSION_EXPIRED' }), { status: 401, headers: { 'X-Correlation-Id': 'c-456' } })
    await expect(getSystemStatus(config, undefined, async () => response)).resolves.toEqual({ health: 'unauthorized', correlationId: 'c-456', errorCode: 'AUTH.SESSION_EXPIRED' })
  })
  it('adds an access token only when an authenticated session supplies one', async () => {
    let authorization: string | null = null
    await getSystemStatus(config, 'token-value', async (_input, init) => {
      authorization = new Headers(init?.headers).get('Authorization')
      return new Response(null, { status: 200 })
    })
    expect(authorization).toBe('Bearer token-value')
  })
})

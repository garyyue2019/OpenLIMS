import { describe, expect, it } from 'vitest'
import { loadRuntimeConfig, RuntimeConfigError } from './runtime-config'

const valid = { apiBaseUrl: '/api', environmentLabel: 'Verification', oidc: { authority: 'https://identity.example.test', clientId: 'openlims-web', scope: 'openid profile' } }
describe('loadRuntimeConfig', () => {
  it('accepts complete same-origin runtime configuration', async () => {
    await expect(loadRuntimeConfig(async () => new Response(JSON.stringify(valid)))).resolves.toEqual(valid)
  })
  it('rejects an unsafe API origin', async () => {
    await expect(loadRuntimeConfig(async () => new Response(JSON.stringify({ ...valid, apiBaseUrl: 'https://example.test' })))).rejects.toBeInstanceOf(RuntimeConfigError)
  })
  it('rejects missing OIDC settings', async () => {
    await expect(loadRuntimeConfig(async () => new Response(JSON.stringify({ apiBaseUrl: '/api', environmentLabel: 'Verification' })))).rejects.toBeInstanceOf(RuntimeConfigError)
  })
  it('accepts an HTTP loopback authority for the disposable local stack only', async () => {
    const local = { ...valid, oidc: { ...valid.oidc, authority: 'http://127.0.0.1:8080/realms/openlims-development' } }
    await expect(loadRuntimeConfig(async () => new Response(JSON.stringify(local)))).resolves.toEqual(local)
  })
  it('rejects a non-loopback HTTP authority', async () => {
    const insecure = { ...valid, oidc: { ...valid.oidc, authority: 'http://identity.example.test' } }
    await expect(loadRuntimeConfig(async () => new Response(JSON.stringify(insecure)))).rejects.toBeInstanceOf(RuntimeConfigError)
  })
})

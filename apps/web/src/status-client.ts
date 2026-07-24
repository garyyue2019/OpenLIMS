import type { RuntimeConfig } from './runtime-config'
export type SystemHealth = 'ready' | 'unavailable' | 'unauthorized' | 'forbidden'
export interface SystemStatus { health: SystemHealth; correlationId?: string; errorCode?: string }
function endpoint(baseUrl: string, path: string): string {
  const normalizedBase = baseUrl.endsWith('/') ? baseUrl.slice(0, -1) : baseUrl
  return new URL(`${normalizedBase}${path.startsWith('/') ? path : `/${path}`}`, window.location.origin).toString()
}
export async function getSystemStatus(config: RuntimeConfig, accessToken?: string, fetcher = fetch): Promise<SystemStatus> {
  try {
    const response = await fetcher(endpoint(config.apiBaseUrl, '/system/status'), {
      credentials: 'same-origin',
      ...(accessToken ? { headers: { Authorization: `Bearer ${accessToken}` } } : {})
    })
    const correlationId = response.headers.get('X-Correlation-Id') ?? undefined
    if (response.ok) return { health: 'ready', correlationId }
    const problem: unknown = await response.json().catch(() => null)
    const errorCode = problem && typeof problem === 'object' && typeof (problem as Record<string, unknown>).errorCode === 'string' ? (problem as Record<string, string>).errorCode : undefined
    if (response.status === 401) return { health: 'unauthorized', correlationId, errorCode }
    if (response.status === 403) return { health: 'forbidden', correlationId, errorCode }
    return { health: 'unavailable', correlationId, errorCode }
  } catch { return { health: 'unavailable' } }
}

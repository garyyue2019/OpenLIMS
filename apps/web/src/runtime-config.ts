export interface RuntimeConfig {
  apiBaseUrl: string
  environmentLabel: string
  oidc: OidcRuntimeConfig
}

export interface OidcRuntimeConfig {
  authority: string
  clientId: string
  scope: string
  audience?: string
}

export class RuntimeConfigError extends Error {
  constructor(message: string) {
    super(message)
    this.name = 'RuntimeConfigError'
  }
}

function isSafeApiBaseUrl(value: unknown): value is string {
  if (typeof value !== 'string' || value.length === 0 || value.length > 200) return false
  try {
    const url = new URL(value, window.location.origin)
    return ['http:', 'https:'].includes(url.protocol) && url.origin === window.location.origin
  } catch {
    return false
  }
}

function isSafeAuthority(value: unknown): value is string {
  if (typeof value !== 'string' || value.length === 0 || value.length > 200) return false
  try {
    const url = new URL(value)
    const secure = url.protocol === 'https:'
    const localDevelopment = url.protocol === 'http:' && isLoopback(url.hostname) && isLoopback(window.location.hostname)
    return (secure || localDevelopment) && Boolean(url.hostname) && !url.username && !url.password
  } catch {
    return false
  }
}

function isLoopback(hostname: string): boolean {
  return hostname === 'localhost' || hostname === '127.0.0.1' || hostname === '[::1]' || hostname === '::1'
}

function hasOpenIdScope(value: unknown): value is string {
  return typeof value === 'string' && value.length <= 300 && value.split(/\s+/).includes('openid')
}

export async function loadRuntimeConfig(fetcher = fetch): Promise<RuntimeConfig> {
  let response: Response
  try {
    response = await fetcher('/runtime-config.json', { cache: 'no-store', credentials: 'same-origin' })
  } catch {
    throw new RuntimeConfigError('Unable to load runtime configuration.')
  }
  if (!response.ok) throw new RuntimeConfigError('Runtime configuration is unavailable.')
  const value: unknown = await response.json().catch(() => null)
  if (!value || typeof value !== 'object') throw new RuntimeConfigError('Runtime configuration has an invalid format.')
  const config = value as Record<string, unknown>
  const oidc = config.oidc as Record<string, unknown> | undefined
  if (!isSafeApiBaseUrl(config.apiBaseUrl) || typeof config.environmentLabel !== 'string' || !config.environmentLabel.trim() ||
    !oidc || !isSafeAuthority(oidc.authority) || typeof oidc.clientId !== 'string' || !oidc.clientId.trim() || !hasOpenIdScope(oidc.scope) ||
    (oidc.audience !== undefined && (typeof oidc.audience !== 'string' || !oidc.audience.trim()))) {
    throw new RuntimeConfigError('Runtime configuration is incomplete or invalid.')
  }
  return {
    apiBaseUrl: config.apiBaseUrl,
    environmentLabel: config.environmentLabel.trim(),
    oidc: {
      authority: oidc.authority,
      clientId: oidc.clientId.trim(),
      scope: oidc.scope,
      ...(typeof oidc.audience === 'string' ? { audience: oidc.audience.trim() } : {})
    }
  }
}

import { UserManager, WebStorageStateStore, type User } from 'oidc-client-ts'
import type { RuntimeConfig } from './runtime-config'

export type AuthStatus = 'loading' | 'authenticated' | 'anonymous' | 'expired' | 'callback-error' | 'configuration-error'

export interface AuthSnapshot { status: AuthStatus; user?: User; returnTo?: string }

export interface OidcManager {
  getUser(): Promise<User | null>
  removeUser(): Promise<void>
  signinRedirect(args?: { state?: { returnTo: string } }): Promise<void>
  signinRedirectCallback(): Promise<User>
  signoutRedirect(): Promise<void>
}

export function createOidcManager(config: RuntimeConfig): UserManager {
  return new UserManager({
    authority: config.oidc.authority,
    client_id: config.oidc.clientId,
    redirect_uri: new URL('/auth/callback', window.location.origin).toString(),
    post_logout_redirect_uri: new URL('/', window.location.origin).toString(),
    response_type: 'code', scope: config.oidc.scope,
    userStore: new WebStorageStateStore({ store: window.sessionStorage }),
    stateStore: new WebStorageStateStore({ store: window.sessionStorage }),
    automaticSilentRenew: true, monitorSession: true, filterProtocolClaims: true, loadUserInfo: false
  })
}

export class AuthController {
  public snapshot: AuthSnapshot = { status: 'loading' }
  constructor(private readonly manager: OidcManager) {}
  async restore(): Promise<AuthSnapshot> {
    try {
      const user = await this.manager.getUser()
      if (!user) return this.set({ status: 'anonymous' })
      if (user.expired) { await this.manager.removeUser(); return this.set({ status: 'expired' }) }
      return this.set({ status: 'authenticated', user })
    } catch { return this.set({ status: 'anonymous' }) }
  }
  async signIn(returnTo = '/'): Promise<void> { await this.manager.signinRedirect({ state: { returnTo: safeReturnTo(returnTo) } }) }
  async completeSignIn(): Promise<AuthSnapshot> {
    try {
      const user = await this.manager.signinRedirectCallback()
      if (user.expired) { await this.manager.removeUser(); return this.set({ status: 'expired' }) }
      return this.set({ status: 'authenticated', user, returnTo: returnToFromState(user.state) })
    } catch { return this.set({ status: 'callback-error' }) }
  }
  async signOut(): Promise<void> { await this.manager.signoutRedirect() }
  private set(snapshot: AuthSnapshot): AuthSnapshot { this.snapshot = snapshot; return snapshot }
}

function safeReturnTo(value: string): string { return value.startsWith('/') && !value.startsWith('//') ? value : '/' }

function returnToFromState(value: unknown): string {
  if (!value || typeof value !== 'object') return '/'
  const returnTo = (value as Record<string, unknown>).returnTo
  return typeof returnTo === 'string' ? safeReturnTo(returnTo) : '/'
}

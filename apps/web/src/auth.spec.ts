import { describe, expect, it, vi } from 'vitest'
import { AuthController, type OidcManager } from './auth'

function manager(user: { expired: boolean } | null): OidcManager {
  return {
    getUser: vi.fn().mockResolvedValue(user), removeUser: vi.fn().mockResolvedValue(undefined),
    signinRedirect: vi.fn().mockResolvedValue(undefined), signinRedirectCallback: vi.fn().mockResolvedValue(user), signoutRedirect: vi.fn().mockResolvedValue(undefined)
  } as unknown as OidcManager
}
describe('AuthController', () => {
  it('restores an active OIDC session', async () => {
    await expect(new AuthController(manager({ expired: false })).restore()).resolves.toMatchObject({ status: 'authenticated' })
  })
  it('removes an expired session', async () => {
    const oidc = manager({ expired: true }); await expect(new AuthController(oidc).restore()).resolves.toEqual({ status: 'expired' }); expect(oidc.removeUser).toHaveBeenCalledOnce()
  })
  it('rejects external return locations', async () => {
    const oidc = manager(null); await new AuthController(oidc).signIn('//example.test'); expect(oidc.signinRedirect).toHaveBeenCalledWith({ state: { returnTo: '/' } })
  })
  it('restores only a local return location from callback state', async () => {
    const local = manager({ expired: false, state: { returnTo: '/system/status' } } as never)
    await expect(new AuthController(local).completeSignIn()).resolves.toMatchObject({ returnTo: '/system/status' })
    const external = manager({ expired: false, state: { returnTo: '//example.test' } } as never)
    await expect(new AuthController(external).completeSignIn()).resolves.toMatchObject({ returnTo: '/' })
  })
})

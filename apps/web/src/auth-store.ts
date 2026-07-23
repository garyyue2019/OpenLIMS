import { readonly, ref } from 'vue'
import { AuthController, createOidcManager, type AuthSnapshot } from './auth'
import { loadRuntimeConfig, type RuntimeConfig } from './runtime-config'

const snapshot = ref<AuthSnapshot>({ status: 'loading' })
const config = ref<RuntimeConfig>()
let controller: AuthController | undefined
export const authSnapshot = readonly(snapshot)
export const runtimeConfig = readonly(config)

export async function initializeAuth(): Promise<void> {
  try { config.value = await loadRuntimeConfig(); controller = new AuthController(createOidcManager(config.value)); snapshot.value = await controller.restore() }
  catch { snapshot.value = { status: 'configuration-error' } }
}
export async function signIn(returnTo = '/'): Promise<void> { if (controller) await controller.signIn(returnTo) }
export async function signOut(): Promise<void> { if (controller) await controller.signOut() }
export async function completeSignIn(): Promise<AuthSnapshot> {
  if (!controller) return { status: 'configuration-error' }
  snapshot.value = await controller.completeSignIn(); return snapshot.value
}

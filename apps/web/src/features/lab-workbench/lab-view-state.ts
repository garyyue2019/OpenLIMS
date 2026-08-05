import { computed } from 'vue'
import { authSnapshot } from '../../auth-store'
import { LabApiError } from './lab-api'
import { hasLabCapability } from './lab-workbench-access'

export function useLabAccess(requiredCapability: string) {
  const authenticated = computed(() => authSnapshot.value.status === 'authenticated')
  const accessToken = computed(() => authSnapshot.value.user?.access_token ?? '')
  const claims = computed(() =>
    authSnapshot.value.user?.profile as Readonly<Record<string, unknown>> | undefined
  )
  const canWrite = computed(() =>
    authenticated.value && hasLabCapability(claims.value, requiredCapability)
  )

  return {
    authStatus: computed(() => authSnapshot.value.status),
    authenticated,
    accessToken,
    canWrite
  }
}

export function normalizeLabError(error: unknown): LabApiError {
  if (error instanceof LabApiError) return error
  if (error && typeof error === 'object') {
    const source = error as Record<string, unknown>
    if (typeof source.errorCode === 'string' && typeof source.status === 'number') {
      return new LabApiError(
        source.errorCode,
        source.status,
        typeof source.correlationId === 'string' ? source.correlationId : 'not-available',
        typeof source.detail === 'string' ? source.detail : undefined,
        typeof source.nextAction === 'string' ? source.nextAction : undefined,
        typeof source.title === 'string' ? source.title : undefined
      )
    }
  }
  return new LabApiError(
    'WEB.UNEXPECTED_ERROR',
    0,
    'not-available',
    'The operation could not be completed.',
    'Review the input and retry explicitly.'
  )
}

export function positiveInteger(value: number): boolean {
  return Number.isInteger(value) && value > 0
}

export function nonNegativeInteger(value: number): boolean {
  return Number.isInteger(value) && value >= 0
}

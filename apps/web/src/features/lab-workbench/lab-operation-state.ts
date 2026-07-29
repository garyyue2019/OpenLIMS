import { ref, type Ref } from 'vue'
import type { LabApiError } from './lab-api'
import { normalizeLabError } from './lab-view-state'

export function useLabOperationState(
  authenticated: Readonly<Ref<boolean>>,
  accessToken: Readonly<Ref<string>>
) {
  const busy = ref(false)
  const validationError = ref('')
  const error = ref<LabApiError>()
  const response = ref<unknown>()
  const responseTitle = ref('服务器响应')
  let retryAction: (() => Promise<void>) | undefined

  function validate(message = ''): boolean {
    validationError.value = message
    return !message
  }

  async function execute<T>(
    title: string,
    action: () => Promise<T>,
    retry: () => Promise<void>
  ): Promise<T | undefined> {
    if (!authenticated.value || !accessToken.value || busy.value) return undefined
    busy.value = true
    validationError.value = ''
    error.value = undefined
    retryAction = retry
    try {
      const value = await action()
      response.value = value
      responseTitle.value = title
      return value
    } catch (caught) {
      error.value = normalizeLabError(caught)
      return undefined
    } finally {
      busy.value = false
    }
  }

  function retryLast(): void {
    if (retryAction && !busy.value) void retryAction()
  }

  return {
    busy,
    validationError,
    error,
    response,
    responseTitle,
    validate,
    execute,
    retryLast
  }
}

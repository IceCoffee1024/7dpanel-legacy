import { onMounted, onUnmounted, readonly, shallowRef } from 'vue'
import { useRouter } from 'vue-router'

import type { PlayerSession } from '../api/playerSession'

import { fetchPlayerSession, logoutPlayerSession } from '../api/playerSession'

export type PlayerSessionStatus = 'loading' | 'authenticated' | 'error'

export function usePlayerSession() {
  const router = useRouter()
  const status = shallowRef<PlayerSessionStatus>('loading')
  const session = shallowRef<PlayerSession | null>(null)
  const isLoggingOut = shallowRef(false)
  const logoutError = shallowRef('')
  const abortController = new AbortController()

  async function load() {
    try {
      const result = await fetchPlayerSession(abortController.signal)
      if (result.kind === 'unauthenticated') {
        await router.replace({ name: 'login', query: { redirect: '/store' } })
        return
      }

      session.value = result.session
      status.value = 'authenticated'
    }
    catch (error) {
      if (error instanceof DOMException && error.name === 'AbortError')
        return

      status.value = 'error'
    }
  }

  async function logout() {
    if (isLoggingOut.value)
      return

    isLoggingOut.value = true
    logoutError.value = ''
    try {
      await logoutPlayerSession(abortController.signal)
      session.value = null
      await router.replace({ name: 'login' })
    }
    catch (error) {
      if (error instanceof DOMException && error.name === 'AbortError')
        return

      logoutError.value = '退出失败，请重试。'
    }
    finally {
      isLoggingOut.value = false
    }
  }

  onMounted(() => void load())
  onUnmounted(() => abortController.abort())

  return {
    status: readonly(status),
    session: readonly(session),
    isLoggingOut: readonly(isLoggingOut),
    logoutError: readonly(logoutError),
    logout,
  }
}

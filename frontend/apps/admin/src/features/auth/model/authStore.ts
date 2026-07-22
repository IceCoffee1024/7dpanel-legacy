import { defineStore } from 'pinia'
import { computed, shallowRef } from 'vue'

import { AuthError, loginWithPassword } from '../api/auth'

export interface AuthStoreDependencies {
  now: () => number
  loginRequest: typeof loginWithPassword
}

export function createAuthStore(dependencies: AuthStoreDependencies) {
  return defineStore('auth', () => {
    const token = shallowRef<string | null>(null)
    const expiresAt = shallowRef<number | null>(null)
    const error = shallowRef<import('../api/auth').AuthErrorCode | null>(null)
    const submitting = shallowRef(false)
    let pendingLogin: Promise<void> | null = null
    let sessionExpiryTimer: ReturnType<typeof setTimeout> | null = null

    function clearSessionExpiryTimer() {
      if (sessionExpiryTimer === null)
        return
      clearTimeout(sessionExpiryTimer)
      sessionExpiryTimer = null
    }

    function expireSession() {
      clearSessionExpiryTimer()
      token.value = null
      expiresAt.value = null
    }

    function hasActiveSession() {
      if (token.value === null || expiresAt.value === null)
        return false
      if (dependencies.now() < expiresAt.value)
        return true

      expireSession()
      return false
    }

    const isAuthenticated = computed(hasActiveSession)
    const authorizationHeader = computed(() => hasActiveSession() ? `Bearer ${token.value}` : null)
    const status = computed(() => {
      if (submitting.value)
        return 'submitting' as const
      return hasActiveSession() ? 'authenticated' as const : 'anonymous' as const
    })

    function login(username: string, password: string): Promise<void> {
      if (pendingLogin !== null)
        return pendingLogin

      expireSession()
      error.value = null
      submitting.value = true

      pendingLogin = dependencies.loginRequest(username, password)
        .then((accessToken) => {
          token.value = accessToken.token
          expiresAt.value = accessToken.expiresAt
          sessionExpiryTimer = setTimeout(expireSession, Math.max(0, accessToken.expiresAt - dependencies.now()))
        })
        .catch((cause: unknown) => {
          error.value = cause instanceof AuthError ? cause.code : 'unavailable'
        })
        .finally(() => {
          submitting.value = false
          pendingLogin = null
        })

      return pendingLogin
    }

    function logout() {
      expireSession()
      error.value = null
    }

    return {
      token,
      expiresAt,
      status,
      error,
      isAuthenticated,
      authorizationHeader,
      login,
      logout,
      expireSession,
    }
  })
}

export const useAuthStore = createAuthStore({
  now: Date.now,
  loginRequest: loginWithPassword,
})

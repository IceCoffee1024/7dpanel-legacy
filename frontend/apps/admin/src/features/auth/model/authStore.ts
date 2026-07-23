import type { AuthRole, AuthSession, SessionPersistence } from './authSession'
import type { AuthSessionRepository } from './authSessionRepository'

import { defineStore } from 'pinia'
import { computed, onScopeDispose, shallowRef } from 'vue'
import { AuthError, loginWithPassword } from '../api/auth'
import { createBrowserAuthSessionRepository } from './authSessionRepository'

export interface AuthStoreDependencies {
  now: () => number
  loginRequest: typeof loginWithPassword
  sessionRepository: AuthSessionRepository
}

export function createAuthStore(dependencies: AuthStoreDependencies) {
  return defineStore('auth', () => {
    const token = shallowRef<string | null>(null)
    const expiresAt = shallowRef<number | null>(null)
    const username = shallowRef<string | null>(null)
    const role = shallowRef<AuthRole | null>(null)
    const error = shallowRef<import('../api/auth').AuthErrorCode | null>(null)
    const persistenceWarning = shallowRef(false)
    const submitting = shallowRef(false)
    let pendingLogin: Promise<void> | null = null
    let sessionExpiryTimer: ReturnType<typeof setTimeout> | null = null

    function clearSessionExpiryTimer() {
      if (sessionExpiryTimer === null)
        return
      clearTimeout(sessionExpiryTimer)
      sessionExpiryTimer = null
    }

    function clearInMemorySession() {
      clearSessionExpiryTimer()
      token.value = null
      expiresAt.value = null
      username.value = null
      role.value = null
    }

    function applySession(session: AuthSession) {
      clearSessionExpiryTimer()
      token.value = session.token
      expiresAt.value = session.expiresAt
      username.value = session.username
      role.value = session.role
      sessionExpiryTimer = setTimeout(
        expireSession,
        Math.max(0, session.expiresAt - dependencies.now()),
      )
    }

    function expireSession() {
      clearInMemorySession()
      dependencies.sessionRepository.clear()
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

    function login(
      loginUsername: string,
      password: string,
      rememberLogin = false,
    ): Promise<void> {
      if (pendingLogin !== null)
        return pendingLogin

      expireSession()
      error.value = null
      persistenceWarning.value = false
      submitting.value = true

      pendingLogin = dependencies.loginRequest(loginUsername, password)
        .then((session) => {
          applySession(session)
          const persistence: SessionPersistence = rememberLogin ? 'browser' : 'tab'
          persistenceWarning.value = !dependencies.sessionRepository.save(session, persistence)
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
      persistenceWarning.value = false
    }

    const restoredSession = dependencies.sessionRepository.restore(dependencies.now())
    if (restoredSession !== null)
      applySession(restoredSession)

    const unsubscribe = dependencies.sessionRepository.subscribe((externalSession) => {
      if (externalSession === null)
        clearInMemorySession()
      else
        applySession(externalSession)
    })
    onScopeDispose(() => {
      clearSessionExpiryTimer()
      unsubscribe()
    })

    return {
      token,
      expiresAt,
      username,
      role,
      status,
      error,
      persistenceWarning,
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
  sessionRepository: createBrowserAuthSessionRepository({
    now: Date.now,
    getLocalStorage: () => window.localStorage,
    getSessionStorage: () => window.sessionStorage,
    eventTarget: window,
  }),
})

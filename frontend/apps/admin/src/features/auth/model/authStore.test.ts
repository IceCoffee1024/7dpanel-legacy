import type { AuthSession } from './authSession'
import type { AuthSessionRepository } from './authSessionRepository'

import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { AuthError } from '../api/auth'
import { createAuthStore } from './authStore'

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason?: unknown) => void
  const promise = new Promise<T>((resolvePromise, rejectPromise) => {
    resolve = resolvePromise
    reject = rejectPromise
  })
  return { promise, resolve, reject }
}

function createFakeRepository(restoredSession: AuthSession | null = null) {
  const listeners = new Set<(session: AuthSession | null) => void>()
  const repository: AuthSessionRepository & {
    emit: (session: AuthSession | null) => void
  } = {
    restore: vi.fn(() => restoredSession),
    save: vi.fn(() => true),
    clear: vi.fn(),
    subscribe: vi.fn((listener) => {
      listeners.add(listener)
      return () => listeners.delete(listener)
    }),
    emit: session => listeners.forEach(listener => listener(session)),
  }

  return repository
}

describe('authStore', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('synchronously restores a complete persisted session', () => {
    const restoredSession: AuthSession = {
      token: '7dp_t_restored.secret',
      expiresAt: 10_000,
      username: 'server-owner',
      role: 'Owner',
    }
    const sessionRepository = createFakeRepository(restoredSession)
    const useStore = createAuthStore({
      now: () => 1_000,
      loginRequest: vi.fn(),
      sessionRepository,
    })

    const store = useStore()

    expect(sessionRepository.restore).toHaveBeenCalledExactlyOnceWith(1_000)
    expect(store.$state).toMatchObject(restoredSession)
    expect(store.authorizationHeader).toBe('Bearer 7dp_t_restored.secret')
    expect(store.isAuthenticated).toBe(true)
  })

  it.each([
    [false, 'tab'],
    [true, 'browser'],
  ] as const)('saves a successful login using %s persistence as %s', async (rememberLogin, persistence) => {
    const authenticatedSession: AuthSession = {
      token: '7dp_t_id.secret',
      expiresAt: 10_000,
      username: 'server-owner',
      role: 'Owner',
    }
    const sessionRepository = createFakeRepository()
    const useStore = createAuthStore({
      now: () => 1_000,
      loginRequest: vi.fn().mockResolvedValue(authenticatedSession),
      sessionRepository,
    })
    const store = useStore()

    await store.login('sensitive-user', 'sensitive-password', rememberLogin)

    expect(sessionRepository.save).toHaveBeenCalledExactlyOnceWith(authenticatedSession, persistence)
    expect(store.persistenceWarning).toBe(false)
  })

  it('keeps an authenticated in-memory session when persistence fails', async () => {
    const authenticatedSession: AuthSession = {
      token: '7dp_t_id.secret',
      expiresAt: 10_000,
      username: 'server-owner',
      role: 'Owner',
    }
    const sessionRepository = createFakeRepository()
    vi.mocked(sessionRepository.save).mockReturnValue(false)
    const useStore = createAuthStore({
      now: () => 1_000,
      loginRequest: vi.fn().mockResolvedValue(authenticatedSession),
      sessionRepository,
    })
    const store = useStore()

    await store.login('sensitive-user', 'sensitive-password', true)

    expect(store.isAuthenticated).toBe(true)
    expect(store.authorizationHeader).toBe('Bearer 7dp_t_id.secret')
    expect(store.persistenceWarning).toBe(true)
  })

  it('replaces the session for an external browser session and clears only memory for deletion', () => {
    const sessionRepository = createFakeRepository()
    const useStore = createAuthStore({
      now: () => 1_000,
      loginRequest: vi.fn(),
      sessionRepository,
    })
    const store = useStore()
    const externalSession: AuthSession = {
      token: '7dp_t_external.secret',
      expiresAt: 10_000,
      username: 'server-admin',
      role: 'Admin',
    }

    sessionRepository.emit(externalSession)
    expect(store.$state).toMatchObject(externalSession)

    vi.mocked(sessionRepository.clear).mockClear()
    sessionRepository.emit(null)

    expect(store.$state).toMatchObject({
      token: null,
      expiresAt: null,
      username: null,
      role: null,
    })
    expect(sessionRepository.clear).not.toHaveBeenCalled()
  })

  it('clears the repository when the session expires or the user logs out', async () => {
    const sessionRepository = createFakeRepository()
    const useStore = createAuthStore({
      now: () => 1_000,
      loginRequest: vi.fn().mockResolvedValue({
        token: '7dp_t_id.secret',
        expiresAt: 10_000,
        username: 'admin',
        role: 'Owner',
      }),
      sessionRepository,
    })
    const store = useStore()
    await store.login('user', 'password')

    vi.mocked(sessionRepository.clear).mockClear()
    store.expireSession()
    expect(sessionRepository.clear).toHaveBeenCalledOnce()

    await store.login('user', 'password')
    vi.mocked(sessionRepository.clear).mockClear()
    store.logout()
    expect(sessionRepository.clear).toHaveBeenCalledOnce()
  })

  it('stops reacting to external sessions when the store is disposed', () => {
    const sessionRepository = createFakeRepository()
    const useStore = createAuthStore({
      now: () => 1_000,
      loginRequest: vi.fn(),
      sessionRepository,
    })
    const store = useStore()
    store.$dispose()

    sessionRepository.emit({
      token: '7dp_t_external.secret',
      expiresAt: 10_000,
      username: 'server-admin',
      role: 'Admin',
    })

    expect(store.isAuthenticated).toBe(false)
    expect(store.authorizationHeader).toBeNull()
  })

  it('keeps only the access token session after a successful login', async () => {
    const loginRequest = vi.fn().mockResolvedValue({
      token: 'opaque-token',
      expiresAt: 10_000,
      username: 'server-owner',
      role: 'Owner',
    })
    const useStore = createAuthStore({ now: () => 1_000, loginRequest, sessionRepository: createFakeRepository() })
    const store = useStore()

    await store.login('sensitive-user', 'sensitive-password')

    expect(store.status).toBe('authenticated')
    expect(store.isAuthenticated).toBe(true)
    expect(store.authorizationHeader).toBe('Bearer opaque-token')
    expect(store.$state).toMatchObject({
      token: 'opaque-token',
      expiresAt: 10_000,
      username: 'server-owner',
      role: 'Owner',
    })
    expect(JSON.stringify(store.$state)).not.toContain('sensitive-user')
    expect(JSON.stringify(store.$state)).not.toContain('sensitive-password')
    expect(store.$state).not.toHaveProperty('password')
  })

  it('treats and clears a known expired session as unauthenticated', async () => {
    let now = 1_000
    const useStore = createAuthStore({
      now: () => now,
      loginRequest: vi.fn().mockResolvedValue({
        token: 'short-lived',
        expiresAt: 2_000,
        username: 'admin',
        role: 'Owner',
      }),
      sessionRepository: createFakeRepository(),
    })
    const store = useStore()
    await store.login('user', 'password')

    now = 2_000

    expect(store.isAuthenticated).toBe(false)
    expect(store.authorizationHeader).toBeNull()
    expect(store.$state).toMatchObject({ token: null, expiresAt: null })
  })

  it('invalidates cached authentication state when the session expires', async () => {
    vi.useFakeTimers()
    let now = 1_000
    const useStore = createAuthStore({
      now: () => now,
      loginRequest: vi.fn().mockResolvedValue({
        token: 'short-lived',
        expiresAt: 2_000,
        username: 'admin',
        role: 'Owner',
      }),
      sessionRepository: createFakeRepository(),
    })
    const store = useStore()

    await store.login('user', 'password')
    expect(store.isAuthenticated).toBe(true)
    expect(store.authorizationHeader).toBe('Bearer short-lived')

    now = 2_000
    vi.advanceTimersByTime(1_000)

    expect(store.isAuthenticated).toBe(false)
    expect(store.authorizationHeader).toBeNull()
    expect(store.$state).toMatchObject({ token: null, expiresAt: null })
    vi.useRealTimers()
  })

  it('does not start a second request while login is submitting', async () => {
    const pending = deferred<AuthSession>()
    const loginRequest = vi.fn().mockReturnValue(pending.promise)
    const useStore = createAuthStore({ now: () => 1_000, loginRequest, sessionRepository: createFakeRepository() })
    const store = useStore()

    const first = store.login('first', 'password-one')
    const second = store.login('second', 'password-two')

    expect(store.status).toBe('submitting')
    expect(loginRequest).toHaveBeenCalledOnce()
    pending.resolve({ token: 'token', expiresAt: 10_000, username: 'admin', role: 'Owner' })
    await Promise.all([first, second])
  })

  it('clears the old session before a new login and does not restore it on failure', async () => {
    const pending = deferred<AuthSession>()
    const loginRequest = vi.fn()
      .mockResolvedValueOnce({ token: 'old-token', expiresAt: 10_000, username: 'admin', role: 'Owner' })
      .mockReturnValueOnce(pending.promise)
    const useStore = createAuthStore({ now: () => 1_000, loginRequest, sessionRepository: createFakeRepository() })
    const store = useStore()
    await store.login('first', 'password-one')

    const replacement = store.login('second', 'password-two')
    expect(store.authorizationHeader).toBeNull()
    expect(store.$state).toMatchObject({ token: null, expiresAt: null })

    pending.reject(new AuthError('invalid-credentials'))
    await replacement

    expect(store.status).toBe('anonymous')
    expect(store.error).toBe('invalid-credentials')
    expect(store.authorizationHeader).toBeNull()
  })

  it('retains only a stable error code when login fails', async () => {
    const loginRequest = vi.fn().mockRejectedValue(new Error('raw sensitive exception text'))
    const useStore = createAuthStore({ now: () => 1_000, loginRequest, sessionRepository: createFakeRepository() })
    const store = useStore()

    await store.login('sensitive-user', 'sensitive-password')

    expect(store.status).toBe('anonymous')
    expect(store.error).toBe('unavailable')
    const serializedState = JSON.stringify(store.$state)
    expect(serializedState).not.toContain('sensitive-user')
    expect(serializedState).not.toContain('sensitive-password')
    expect(serializedState).not.toContain('raw sensitive exception text')
  })

  it('logout and expireSession clear the in-memory session', async () => {
    const useStore = createAuthStore({
      now: () => 1_000,
      loginRequest: vi.fn().mockResolvedValue({
        token: 'token',
        expiresAt: 10_000,
        username: 'admin',
        role: 'Owner',
      }),
      sessionRepository: createFakeRepository(),
    })
    const store = useStore()
    await store.login('user', 'password')

    store.expireSession()
    expect(store.$state).toMatchObject({ token: null, expiresAt: null })

    await store.login('user', 'password')
    store.logout()
    expect(store.status).toBe('anonymous')
    expect(store.error).toBeNull()
    expect(store.$state).toMatchObject({ token: null, expiresAt: null })
  })

  it('does not restore session data in a fresh Pinia instance', async () => {
    const useStore = createAuthStore({
      now: () => 1_000,
      loginRequest: vi.fn().mockResolvedValue({
        token: 'token',
        expiresAt: 10_000,
        username: 'admin',
        role: 'Owner',
      }),
      sessionRepository: createFakeRepository(),
    })
    await useStore().login('user', 'password')

    setActivePinia(createPinia())
    const freshStore = useStore()

    expect(freshStore.status).toBe('anonymous')
    expect(freshStore.isAuthenticated).toBe(false)
    expect(freshStore.authorizationHeader).toBeNull()
    expect(freshStore.$state).toMatchObject({ token: null, expiresAt: null })
  })
})

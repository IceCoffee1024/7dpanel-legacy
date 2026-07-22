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

describe('authStore', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('keeps only the access token session after a successful login', async () => {
    const loginRequest = vi.fn().mockResolvedValue({ token: 'opaque-token', expiresAt: 10_000 })
    const useStore = createAuthStore({ now: () => 1_000, loginRequest })
    const store = useStore()

    await store.login('sensitive-user', 'sensitive-password')

    expect(store.status).toBe('authenticated')
    expect(store.isAuthenticated).toBe(true)
    expect(store.authorizationHeader).toBe('Bearer opaque-token')
    expect(store.$state).toMatchObject({ token: 'opaque-token', expiresAt: 10_000 })
    expect(JSON.stringify(store.$state)).not.toContain('sensitive-user')
    expect(JSON.stringify(store.$state)).not.toContain('sensitive-password')
    expect(store.$state).not.toHaveProperty('username')
    expect(store.$state).not.toHaveProperty('password')
  })

  it('treats and clears a known expired session as unauthenticated', async () => {
    let now = 1_000
    const useStore = createAuthStore({
      now: () => now,
      loginRequest: vi.fn().mockResolvedValue({ token: 'short-lived', expiresAt: 2_000 }),
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
      loginRequest: vi.fn().mockResolvedValue({ token: 'short-lived', expiresAt: 2_000 }),
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
    const pending = deferred<{ token: string, expiresAt: number }>()
    const loginRequest = vi.fn().mockReturnValue(pending.promise)
    const useStore = createAuthStore({ now: () => 1_000, loginRequest })
    const store = useStore()

    const first = store.login('first', 'password-one')
    const second = store.login('second', 'password-two')

    expect(store.status).toBe('submitting')
    expect(loginRequest).toHaveBeenCalledOnce()
    pending.resolve({ token: 'token', expiresAt: 10_000 })
    await Promise.all([first, second])
  })

  it('clears the old session before a new login and does not restore it on failure', async () => {
    const pending = deferred<{ token: string, expiresAt: number }>()
    const loginRequest = vi.fn()
      .mockResolvedValueOnce({ token: 'old-token', expiresAt: 10_000 })
      .mockReturnValueOnce(pending.promise)
    const useStore = createAuthStore({ now: () => 1_000, loginRequest })
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
    const useStore = createAuthStore({ now: () => 1_000, loginRequest })
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
      loginRequest: vi.fn().mockResolvedValue({ token: 'token', expiresAt: 10_000 }),
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
      loginRequest: vi.fn().mockResolvedValue({ token: 'token', expiresAt: 10_000 }),
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

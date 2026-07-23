import type { AuthSession } from './authSession'

import { describe, expect, it, vi } from 'vitest'
import { serializeAuthSession } from './authSession'
import {
  AUTH_SESSION_STORAGE_KEY,
  createBrowserAuthSessionRepository,
} from './authSessionRepository'

const session: AuthSession = {
  token: '7dp_t_id.secret',
  expiresAt: 2_000,
  username: 'admin',
  role: 'Owner',
}

class MemoryStorage implements Storage {
  private readonly entries = new Map<string, string>()

  get length() {
    return this.entries.size
  }

  clear() {
    this.entries.clear()
  }

  getItem(key: string) {
    return this.entries.get(key) ?? null
  }

  key(index: number) {
    return [...this.entries.keys()][index] ?? null
  }

  removeItem(key: string) {
    this.entries.delete(key)
  }

  setItem(key: string, value: string) {
    this.entries.set(key, value)
  }
}

class FailingStorage extends MemoryStorage {
  override getItem(_key: string): string | null {
    throw new DOMException('Storage unavailable', 'SecurityError')
  }

  override removeItem(_key: string) {
    throw new DOMException('Storage unavailable', 'SecurityError')
  }

  override setItem(_key: string, _value: string) {
    throw new DOMException('Storage unavailable', 'SecurityError')
  }
}

function createRepository() {
  const localStorage = new MemoryStorage()
  const sessionStorage = new MemoryStorage()
  const eventTarget = new EventTarget()
  const repository = createBrowserAuthSessionRepository({
    now: () => 1_000,
    getLocalStorage: () => localStorage,
    getSessionStorage: () => sessionStorage,
    eventTarget,
  })

  return { eventTarget, localStorage, repository, sessionStorage }
}

describe('createBrowserAuthSessionRepository', () => {
  it('saves a tab session only in session storage', () => {
    const { localStorage, repository, sessionStorage } = createRepository()

    expect(repository.save(session, 'tab')).toBe(true)
    expect(sessionStorage.getItem(AUTH_SESSION_STORAGE_KEY)).not.toBeNull()
    expect(localStorage.getItem(AUTH_SESSION_STORAGE_KEY)).toBeNull()
  })

  it('saves a browser session only in local storage', () => {
    const { localStorage, repository, sessionStorage } = createRepository()

    expect(repository.save(session, 'browser')).toBe(true)
    expect(localStorage.getItem(AUTH_SESSION_STORAGE_KEY)).not.toBeNull()
    expect(sessionStorage.getItem(AUTH_SESSION_STORAGE_KEY)).toBeNull()
  })

  it('prefers a valid browser session and removes a stale tab session during restore', () => {
    const { localStorage, repository, sessionStorage } = createRepository()
    const browserSession = { ...session, token: '7dp_t_browser.secret' }

    localStorage.setItem(AUTH_SESSION_STORAGE_KEY, serializeAuthSession(browserSession))
    sessionStorage.setItem(AUTH_SESSION_STORAGE_KEY, serializeAuthSession(session))

    expect(repository.restore(1_000)).toEqual(browserSession)
    expect(sessionStorage.getItem(AUTH_SESSION_STORAGE_KEY)).toBeNull()
  })

  it('clears an invalid browser record before restoring a valid tab session', () => {
    const { localStorage, repository, sessionStorage } = createRepository()

    localStorage.setItem(AUTH_SESSION_STORAGE_KEY, '{')
    sessionStorage.setItem(AUTH_SESSION_STORAGE_KEY, serializeAuthSession(session))

    expect(repository.restore(1_000)).toEqual(session)
    expect(localStorage.getItem(AUTH_SESSION_STORAGE_KEY)).toBeNull()
  })

  it('degrades without throwing when both browser storage getters are unavailable', () => {
    const unavailableStorage = () => {
      throw new DOMException('Storage unavailable', 'SecurityError')
    }
    const repository = createBrowserAuthSessionRepository({
      now: () => 1_000,
      getLocalStorage: unavailableStorage,
      getSessionStorage: unavailableStorage,
      eventTarget: new EventTarget(),
    })

    expect(repository.restore(1_000)).toBeNull()
    expect(repository.save(session, 'browser')).toBe(false)
    expect(() => repository.clear()).not.toThrow()
  })

  it('degrades without throwing when browser storage operations are unavailable', () => {
    const failingStorage = new FailingStorage()
    const repository = createBrowserAuthSessionRepository({
      now: () => 1_000,
      getLocalStorage: () => failingStorage,
      getSessionStorage: () => failingStorage,
      eventTarget: new EventTarget(),
    })

    expect(repository.restore(1_000)).toBeNull()
    expect(repository.save(session, 'tab')).toBe(false)
    expect(() => repository.clear()).not.toThrow()
  })

  it('clears the local tab session and notifies a replacement browser session', () => {
    const { eventTarget, localStorage, repository, sessionStorage } = createRepository()
    const browserSession = { ...session, token: '7dp_t_replacement.secret' }
    const listener = vi.fn()
    repository.subscribe(listener)
    sessionStorage.setItem(AUTH_SESSION_STORAGE_KEY, serializeAuthSession(session))

    eventTarget.dispatchEvent(new StorageEvent('storage', {
      key: AUTH_SESSION_STORAGE_KEY,
      newValue: serializeAuthSession(browserSession),
      storageArea: localStorage,
    }))

    expect(sessionStorage.getItem(AUTH_SESSION_STORAGE_KEY)).toBeNull()
    expect(listener).toHaveBeenCalledExactlyOnceWith(browserSession)
  })

  it('clears the local tab session and notifies null for a deleted browser session', () => {
    const { eventTarget, localStorage, repository, sessionStorage } = createRepository()
    const listener = vi.fn()
    repository.subscribe(listener)
    sessionStorage.setItem(AUTH_SESSION_STORAGE_KEY, serializeAuthSession(session))

    eventTarget.dispatchEvent(new StorageEvent('storage', {
      key: AUTH_SESSION_STORAGE_KEY,
      newValue: null,
      storageArea: localStorage,
    }))

    expect(sessionStorage.getItem(AUTH_SESSION_STORAGE_KEY)).toBeNull()
    expect(listener).toHaveBeenCalledExactlyOnceWith(null)
  })

  it('ignores unrelated storage events and stops notifying after unsubscribe', () => {
    const { eventTarget, localStorage, repository, sessionStorage } = createRepository()
    const listener = vi.fn()
    const unsubscribe = repository.subscribe(listener)

    eventTarget.dispatchEvent(new StorageEvent('storage', {
      key: 'unrelated-key',
      newValue: serializeAuthSession(session),
      storageArea: localStorage,
    }))
    eventTarget.dispatchEvent(new StorageEvent('storage', {
      key: AUTH_SESSION_STORAGE_KEY,
      newValue: serializeAuthSession(session),
      storageArea: sessionStorage,
    }))
    unsubscribe()
    eventTarget.dispatchEvent(new StorageEvent('storage', {
      key: AUTH_SESSION_STORAGE_KEY,
      newValue: serializeAuthSession(session),
      storageArea: localStorage,
    }))

    expect(listener).not.toHaveBeenCalled()
  })
})

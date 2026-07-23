import type { AuthSession, SessionPersistence } from './authSession'
import {

  parseAuthSession,
  serializeAuthSession,

} from './authSession'

export const AUTH_SESSION_STORAGE_KEY = '7dpanel.auth.session.v1'

export interface AuthSessionRepository {
  restore: (now: number) => AuthSession | null
  save: (session: AuthSession, persistence: SessionPersistence) => boolean
  clear: () => void
  subscribe: (listener: (session: AuthSession | null) => void) => () => void
}

export interface BrowserAuthSessionRepositoryOptions {
  now: () => number
  getLocalStorage: () => Storage
  getSessionStorage: () => Storage
  eventTarget: Pick<Window, 'addEventListener' | 'removeEventListener'>
}

function removeStoredSession(getStorage: () => Storage) {
  try {
    const storage = getStorage()
    try {
      storage.removeItem(AUTH_SESSION_STORAGE_KEY)
    }
    catch {}
  }
  catch {}
}

function writeStoredSession(getStorage: () => Storage, value: string) {
  try {
    const storage = getStorage()
    try {
      storage.setItem(AUTH_SESSION_STORAGE_KEY, value)
      return true
    }
    catch {
      return false
    }
  }
  catch {
    return false
  }
}

function readStoredValue(getStorage: () => Storage): string | null {
  try {
    const storage = getStorage()
    try {
      return storage.getItem(AUTH_SESSION_STORAGE_KEY)
    }
    catch {
      return null
    }
  }
  catch {
    return null
  }
}

function getStorageSafely(getStorage: () => Storage): Storage | null {
  try {
    return getStorage()
  }
  catch {
    return null
  }
}

export function createBrowserAuthSessionRepository(
  options: BrowserAuthSessionRepositoryOptions,
): AuthSessionRepository {
  function clear() {
    removeStoredSession(options.getLocalStorage)
    removeStoredSession(options.getSessionStorage)
  }

  return {
    restore(now) {
      const browserValue = readStoredValue(options.getLocalStorage)
      const browserSession = parseAuthSession(browserValue, now)
      if (browserSession !== null) {
        removeStoredSession(options.getSessionStorage)
        return browserSession
      }

      if (browserValue !== null)
        removeStoredSession(options.getLocalStorage)

      const tabValue = readStoredValue(options.getSessionStorage)
      const tabSession = parseAuthSession(tabValue, now)
      if (tabSession === null && tabValue !== null)
        removeStoredSession(options.getSessionStorage)

      return tabSession
    },
    save(session, persistence) {
      clear()
      return writeStoredSession(
        persistence === 'browser' ? options.getLocalStorage : options.getSessionStorage,
        serializeAuthSession(session),
      )
    },
    clear,
    subscribe(listener) {
      function onStorage(event: StorageEvent) {
        if (event.key !== AUTH_SESSION_STORAGE_KEY)
          return

        const localStorage = getStorageSafely(options.getLocalStorage)
        if (localStorage !== null && event.storageArea !== localStorage)
          return

        removeStoredSession(options.getSessionStorage)
        listener(parseAuthSession(event.newValue, options.now()))
      }

      options.eventTarget.addEventListener('storage', onStorage)
      return () => options.eventTarget.removeEventListener('storage', onStorage)
    },
  }
}

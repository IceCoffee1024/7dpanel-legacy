import { describe, expect, it, vi } from 'vitest'

import {
  createBrowserLocalePreferenceRepository,
  LOCALE_PREFERENCE_STORAGE_KEY,
  parseLocalePreference,
  serializeLocalePreference,
} from './localePreference'

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

function createRepository(browserLanguages: readonly string[] = ['en-US']) {
  const storage = new MemoryStorage()
  const eventTarget = new EventTarget()
  const repository = createBrowserLocalePreferenceRepository({
    getStorage: () => storage,
    eventTarget,
    browserLanguages: () => browserLanguages,
  })

  return { eventTarget, repository, storage }
}

describe('locale preference codec', () => {
  it('round trips supported locales', () => {
    expect(parseLocalePreference(serializeLocalePreference('zh-CN'))).toBe('zh-CN')
    expect(parseLocalePreference(serializeLocalePreference('en'))).toBe('en')
  })

  it.each([
    null,
    '{',
    '{}',
    '{"version":2,"locale":"en"}',
    '{"version":1,"locale":"fr"}',
    '{"version":1,"locale":"en","extra":true}',
  ])('rejects invalid value %s', (value) => {
    expect(parseLocalePreference(value)).toBeNull()
  })
})

describe('createBrowserLocalePreferenceRepository', () => {
  it('prefers a valid stored locale over browser languages', () => {
    const { repository, storage } = createRepository(['en-US'])
    storage.setItem(LOCALE_PREFERENCE_STORAGE_KEY, serializeLocalePreference('zh-CN'))

    expect(repository.restore()).toBe('zh-CN')
  })

  it('removes an invalid record and negotiates browser languages', () => {
    const { repository, storage } = createRepository(['fr-FR', 'zh-Hans'])
    storage.setItem(LOCALE_PREFERENCE_STORAGE_KEY, '{invalid')

    expect(repository.restore()).toBe('zh-CN')
    expect(storage.getItem(LOCALE_PREFERENCE_STORAGE_KEY)).toBeNull()
  })

  it('saves a supported locale', () => {
    const { repository, storage } = createRepository()

    expect(repository.save('zh-CN')).toBe(true)
    expect(storage.getItem(LOCALE_PREFERENCE_STORAGE_KEY)).toBe(
      serializeLocalePreference('zh-CN'),
    )
  })

  it('degrades to browser negotiation when storage is unavailable', () => {
    const repository = createBrowserLocalePreferenceRepository({
      getStorage: () => new FailingStorage(),
      eventTarget: new EventTarget(),
      browserLanguages: () => ['zh-CN'],
    })

    expect(repository.restore()).toBe('zh-CN')
    expect(repository.save('en')).toBe(false)
  })

  it('degrades when the storage getter is unavailable', () => {
    const repository = createBrowserLocalePreferenceRepository({
      getStorage: () => {
        throw new DOMException('Storage unavailable', 'SecurityError')
      },
      eventTarget: new EventTarget(),
      browserLanguages: () => ['en-US'],
    })

    expect(repository.restore()).toBe('en')
    expect(repository.save('zh-CN')).toBe(false)
  })

  it('notifies valid external changes without writing them back', () => {
    const { eventTarget, repository, storage } = createRepository()
    const listener = vi.fn()
    const setItem = vi.spyOn(storage, 'setItem')
    repository.subscribe(listener)

    eventTarget.dispatchEvent(new StorageEvent('storage', {
      key: LOCALE_PREFERENCE_STORAGE_KEY,
      newValue: serializeLocalePreference('zh-CN'),
      storageArea: storage,
    }))

    expect(listener).toHaveBeenCalledExactlyOnceWith('zh-CN')
    expect(setItem).not.toHaveBeenCalled()
  })

  it.each([null, '{invalid'])('renegotiates for external value %s', (newValue) => {
    const { eventTarget, repository, storage } = createRepository(['zh-Hans'])
    const listener = vi.fn()
    repository.subscribe(listener)

    eventTarget.dispatchEvent(new StorageEvent('storage', {
      key: LOCALE_PREFERENCE_STORAGE_KEY,
      newValue,
      storageArea: storage,
    }))

    expect(listener).toHaveBeenCalledExactlyOnceWith('zh-CN')
  })

  it('ignores unrelated storage events and stops after unsubscribe', () => {
    const { eventTarget, repository, storage } = createRepository()
    const listener = vi.fn()
    const unsubscribe = repository.subscribe(listener)

    eventTarget.dispatchEvent(new StorageEvent('storage', {
      key: 'other-key',
      newValue: serializeLocalePreference('zh-CN'),
      storageArea: storage,
    }))
    unsubscribe()
    eventTarget.dispatchEvent(new StorageEvent('storage', {
      key: LOCALE_PREFERENCE_STORAGE_KEY,
      newValue: serializeLocalePreference('zh-CN'),
      storageArea: storage,
    }))

    expect(listener).not.toHaveBeenCalled()
  })
})

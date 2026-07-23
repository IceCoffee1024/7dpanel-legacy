import { describe, expect, it } from 'vitest'

import {
  AUTH_SESSION_STORAGE_KEY,
  createBrowserAuthSessionRepository,
  parseAuthSession,
  serializeAuthSession,
} from './index'

describe('auth feature public API', () => {
  it('exports the versioned browser session API', () => {
    expect(AUTH_SESSION_STORAGE_KEY).toBe('7dpanel.auth.session.v1')
    expect(parseAuthSession).toBeTypeOf('function')
    expect(serializeAuthSession).toBeTypeOf('function')
    expect(createBrowserAuthSessionRepository).toBeTypeOf('function')
  })
})

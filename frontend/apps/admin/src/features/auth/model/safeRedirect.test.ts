import { describe, expect, it } from 'vitest'
import { createMemoryHistory, createRouter } from 'vue-router'

import { resolveSafeRedirect } from './safeRedirect'

function createTestRouter() {
  return createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', component: { template: '<div />' } },
      { path: '/login', component: { template: '<div />' } },
      { path: '/players', component: { template: '<div />' } },
    ],
  })
}

describe('resolveSafeRedirect', () => {
  const acceptedRedirects = [
    '/players',
    '/?from=players',
  ]

  it.each(acceptedRedirects)('accepts the internal route %s', (redirect) => {
    expect(resolveSafeRedirect(redirect, createTestRouter())).toBe(redirect)
  })

  const rejectedRedirects: unknown[] = [
    '//evil',
    'https://evil',
    'players',
    '',
    null,
    undefined,
    '/missing',
    '/login',
  ]

  it.each(rejectedRedirects)('falls back for unsafe redirect %s', (redirect) => {
    expect(resolveSafeRedirect(redirect, createTestRouter())).toBe('/players')
  })
})

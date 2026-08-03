import type { RouteMeta } from 'vue-router'
import { describe, expect, it } from 'vitest'

import { canAccessRoute, createRouterRouteAdapter } from './routeAccess'

describe('canAccessRoute', () => {
  it('requires an authenticated session for protected routes', () => {
    const meta: RouteMeta = { requiresAuth: true }

    expect(canAccessRoute(meta, null, false)).toBe(false)
    expect(canAccessRoute(meta, 'Viewer', true)).toBe(true)
  })

  it.each(['Owner', 'Admin', 'Viewer'] as const)('allows %s when the role is listed', (role) => {
    expect(canAccessRoute({ requiresAuth: true, roles: [role] }, role, true)).toBe(true)
  })

  it('denies a role missing from route metadata without changing the target decision', () => {
    expect(canAccessRoute({ requiresAuth: true, roles: ['Owner', 'Admin'] }, 'Viewer', true)).toBe(false)
  })

  it('keeps malformed role metadata permissive to preserve the existing router behavior', () => {
    expect(canAccessRoute({ roles: ['Operator'] as never }, 'Viewer', true)).toBe(true)
  })
})

describe('createRouterRouteAdapter', () => {
  it('indexes route metadata by generated route name', () => {
    const adapter = createRouterRouteAdapter({
      getRoutes: () => [{ name: '/players/', meta: { requiresAuth: true } }] as never,
    })

    expect(adapter.getMeta('/players/')).toEqual({ requiresAuth: true })
    expect(adapter.getMeta('/system/api-keys')).toBeUndefined()
  })
})

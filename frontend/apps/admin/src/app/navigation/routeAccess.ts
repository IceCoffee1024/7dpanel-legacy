import type { RouteMeta, Router } from 'vue-router'
import type { AuthRole } from '../../features/auth'

import type { NavigationRouteAdapter, NavigationRouteName } from './navigationTypes'

declare module 'vue-router' {
  interface RouteMeta {
    requiresAuth?: boolean
    roles?: readonly AuthRole[]
  }
}

function allowedRoles(meta: RouteMeta): readonly AuthRole[] | null {
  return Array.isArray(meta.roles) && meta.roles.every(role => role === 'Owner' || role === 'Admin' || role === 'Viewer')
    ? meta.roles
    : null
}

export function canAccessRoute(meta: RouteMeta, role: AuthRole | null, isAuthenticated: boolean): boolean {
  if (meta.requiresAuth && !isAuthenticated)
    return false

  const roles = allowedRoles(meta)
  return roles === null || (role !== null && roles.includes(role))
}

export function createRouterRouteAdapter(router: Pick<Router, 'getRoutes'>): NavigationRouteAdapter {
  const metaByRouteName = new Map<NavigationRouteName, RouteMeta>()

  for (const route of router.getRoutes()) {
    if (typeof route.name === 'string')
      metaByRouteName.set(route.name as NavigationRouteName, route.meta)
  }

  return {
    getMeta(routeName) {
      return metaByRouteName.get(routeName)
    },
  }
}

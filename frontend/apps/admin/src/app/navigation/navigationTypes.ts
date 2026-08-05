import type { RouteMeta } from 'vue-router'
import type { RouteNamedMap } from 'vue-router/auto-routes'

import type { AuthRole } from '../../features/auth'

export type NavigationGroupId = 'overview' | 'operations' | 'players' | 'community' | 'economy' | 'system'
export type NavigationSectionId
  = 'players-core'
    | 'operations-automation'
    | 'operations-extensions'
    | 'community-chat'
    | 'economy-rewards'
    | 'economy-commerce'
    | 'system-integrations'
export type NavigationRouteName = keyof RouteNamedMap

export interface NavigationEntry {
  readonly id: string
  readonly routeName: NavigationRouteName
  readonly labelKey: string
  readonly icon: string
  readonly searchable?: boolean
  readonly shortcut?: string
  readonly primary?: boolean
  readonly sectionId?: NavigationSectionId
}

export interface NavigationGroup {
  readonly id: NavigationGroupId
  readonly labelKey: string
  readonly icon: string
  readonly children: readonly NavigationEntry[]
}

export interface NavigationRouteParent {
  readonly routeName: NavigationRouteName
  readonly parentRouteName: NavigationRouteName
  readonly labelKey: string
}

export interface NavigationCatalog {
  readonly groups: readonly NavigationGroup[]
  readonly routeParents: readonly NavigationRouteParent[]
}

export interface NavigationRouteAdapter {
  getMeta: (routeName: NavigationRouteName) => RouteMeta | undefined
}

export interface NavigationRouteSnapshot {
  readonly name: NavigationRouteName | undefined
}

export interface NavigationAccessContext {
  readonly role: AuthRole | null
  readonly isAuthenticated: boolean
}

export interface NavigationEntryProjection extends NavigationEntry {
  readonly groupId: NavigationGroupId
}

export interface NavigationGroupProjection {
  readonly id: NavigationGroupId
  readonly labelKey: string
  readonly icon: string
  readonly children: readonly NavigationEntryProjection[]
}

export interface NavigationBreadcrumb {
  readonly routeName: NavigationRouteName
  readonly labelKey: string
}

export interface NavigationShortcut {
  readonly shortcut: string
  readonly routeName: NavigationRouteName
}

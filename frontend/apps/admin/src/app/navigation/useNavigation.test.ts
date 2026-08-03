import type { NavigationAccessContext, NavigationRouteAdapter, NavigationRouteName, NavigationRouteSnapshot } from './navigationTypes'
import { describe, expect, it } from 'vitest'

import { shallowRef } from 'vue'
import { navigationCatalog } from './navigationCatalog'
import { useNavigation } from './useNavigation'

function createRouteAdapter(): NavigationRouteAdapter {
  const entries = navigationCatalog.groups.flatMap(group => group.children)
  const parents = navigationCatalog.routeParents
  const ownerOnly = new Set<NavigationRouteName>([
    '/operations/server',
    '/operations/backups',
    '/operations/automation/schedules',
    '/operations/automation/rules',
    '/operations/configuration',
    '/operations/extensions/mods',
    '/operations/extensions/modules',
    '/operations/world',
    '/players/history/',
    '/players/map',
    '/community/chat/live',
    '/community/teleport',
    '/community/votes',
    '/community/cities',
    '/economy/accounts',
    '/economy/transactions',
    '/economy/rewards/packages',
    '/economy/rewards/daily',
    '/economy/rewards/operations',
    '/economy/rewards/achievements',
    '/economy/commerce/shop',
    '/economy/commerce/redeem-codes',
    '/system/access',
    '/system/integrations/discord',
    '/system/integrations/geoip',
    '/system/audit',
  ])
  const adminOnly = new Set<NavigationRouteName>(['/operations/console'])

  return {
    getMeta(routeName) {
      if (!entries.some(entry => entry.routeName === routeName) && !parents.some(parent => parent.routeName === routeName))
        return undefined
      if (ownerOnly.has(routeName))
        return { requiresAuth: true, roles: ['Owner'] }
      if (adminOnly.has(routeName))
        return { requiresAuth: true, roles: ['Owner', 'Admin'] }
      return { requiresAuth: true }
    },
  }
}

function createNavigation(routeName: NavigationRouteName, context: NavigationAccessContext) {
  const route = shallowRef<NavigationRouteSnapshot>({ name: routeName })
  const access = shallowRef(context)
  return {
    route,
    access,
    navigation: useNavigation({ route, access, routeAdapter: createRouteAdapter() }),
  }
}

describe('useNavigation', () => {
  it('derives all six task domains for an authenticated Owner', () => {
    const { navigation } = createNavigation('/', { role: 'Owner', isAuthenticated: true })

    expect(navigation.groups.value.map(group => group.id))
      .toEqual(['overview', 'operations', 'players', 'community', 'economy', 'system'])
  })

  it('hides groups with no reachable child and does not duplicate child role rules', () => {
    const { navigation } = createNavigation('/players/', { role: 'Viewer', isAuthenticated: true })

    expect(navigation.groups.value.map(group => group.id)).toEqual(['overview', 'players', 'system'])
    expect(navigation.groups.value.find(group => group.id === 'players')?.children.map(child => child.id))
      .toEqual(['online-players', 'access-lists', 'game-resources'])
  })

  it('derives the active domain, current children, search items, and shortcuts from one catalog', () => {
    const { navigation } = createNavigation('/operations/console', { role: 'Admin', isAuthenticated: true })

    expect(navigation.activeGroupId.value).toBe('operations')
    expect(navigation.currentGroup.value?.children.map(child => child.id)).toEqual(['console'])
    expect(navigation.searchItems.value.map(item => item.routeName)).toContain('/operations/console')
    expect(navigation.shortcuts.value).toContainEqual({ shortcut: 'g-c', routeName: '/operations/console' })
    expect(navigation.shortcuts.value).not.toContainEqual({ shortcut: 'g-g', routeName: '/community/chat/live' })
  })

  it('builds dynamic-detail and chat breadcrumbs through the catalog parent chain', () => {
    const history = createNavigation('/players/history/[crossplatformId]', { role: 'Owner', isAuthenticated: true })
    const chat = createNavigation('/community/chat/history', { role: 'Owner', isAuthenticated: true })

    expect(history.navigation.breadcrumbs.value.map(item => item.labelKey))
      .toEqual(['players.navigation', 'players.profile.navigation', 'players.profile.detail'])
    expect(chat.navigation.breadcrumbs.value.map(item => item.labelKey))
      .toEqual(['shell.community', 'gameChat.title', 'gameChat.history.title'])
  })

  it('does not duplicate a task domain when its child uses the same label', () => {
    const overview = createNavigation('/', { role: 'Owner', isAuthenticated: true })
    const players = createNavigation('/players/', { role: 'Viewer', isAuthenticated: true })

    expect(overview.navigation.breadcrumbs.value.map(item => item.labelKey))
      .toEqual(['overview.title'])
    expect(players.navigation.breadcrumbs.value.map(item => item.labelKey))
      .toEqual(['players.navigation'])
  })

  it('recomputes its readonly projections when the session access context changes', () => {
    const { access, navigation } = createNavigation('/players/', { role: 'Owner', isAuthenticated: true })

    access.value = { role: 'Viewer', isAuthenticated: true }

    expect(navigation.groups.value.map(group => group.id)).toEqual(['overview', 'players', 'system'])
    expect(Object.isFrozen(navigation.groups.value)).toBe(true)
    expect(Object.isFrozen(navigation.groups.value[0]!.children)).toBe(true)
  })
})

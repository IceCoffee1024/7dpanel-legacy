import { describe, expect, it } from 'vitest'

import { navigationCatalog } from './navigationCatalog'

describe('navigationCatalog', () => {
  it('keeps the six task domains in their stable order', () => {
    expect(navigationCatalog.groups.map(group => group.id))
      .toEqual(['overview', 'operations', 'players', 'community', 'economy', 'system'])
  })

  it('assigns each sidebar route to exactly one primary entry', () => {
    const routeNames = navigationCatalog.groups.flatMap(group => group.children.map(child => child.routeName))

    expect(new Set(routeNames).size).toBe(routeNames.length)
  })

  it('keeps dynamic details and non-sidebar route variants in parent links', () => {
    const sidebarRoutes = new Set(navigationCatalog.groups.flatMap(group => group.children.map(child => child.routeName)))

    expect(navigationCatalog.routeParents.map(parent => parent.routeName)).toEqual(expect.arrayContaining([
      '/players/history/[crossplatformId]',
      '/players/profile/[crossplatformId]',
      '/community/chat/history',
      '/community/chat/mutes',
    ]))
    expect(navigationCatalog.routeParents.every(parent => !sidebarRoutes.has(parent.routeName))).toBe(true)
  })
})

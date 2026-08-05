import { describe, expect, it } from 'vitest'

import { navigationCatalog } from './navigationCatalog'

describe('navigationCatalog', () => {
  it('keeps the six task domains in their stable order', () => {
    expect(navigationCatalog.groups.map(group => group.id))
      .toEqual(['overview', 'operations', 'players', 'community', 'economy', 'system'])
  })

  it('assigns each sidebar route to exactly one primary entry', () => {
    const routeNames = navigationCatalog.groups.flatMap(group => group.children
      .filter(child => child.primary !== false)
      .map(child => child.routeName))

    expect(new Set(routeNames).size).toBe(routeNames.length)
  })

  it('keeps fixed task entries within the density budget and groups local tabs', () => {
    expect(navigationCatalog.groups.map(group => ({
      id: group.id,
      children: group.children.filter(child => child.primary !== false).map(child => child.id),
    }))).toEqual([
      { id: 'overview', children: ['overview'] },
      { id: 'operations', children: ['server', 'backups', 'schedules', 'configuration', 'mods', 'world', 'console'] },
      { id: 'players', children: ['online-players', 'player-history', 'player-map', 'access-lists'] },
      { id: 'community', children: ['game-chat', 'teleport', 'votes', 'cities'] },
      { id: 'economy', children: ['economy-accounts', 'economy-transactions', 'reward-packages', 'shop'] },
      { id: 'system', children: ['permissions', 'api-keys', 'discord', 'audit'] },
    ])
    expect(navigationCatalog.groups.every(group => group.children.filter(child => child.primary !== false).length <= 7)).toBe(true)
    expect(navigationCatalog.groups.flatMap(group => group.children)
      .filter(child => child.sectionId === 'operations-automation')
      .map(child => child.routeName))
      .toEqual(['/operations/automation/schedules', '/operations/automation/rules'])
    expect(navigationCatalog.groups.flatMap(group => group.children)
      .filter(child => child.sectionId === 'economy-rewards')
      .map(child => child.routeName))
      .toEqual([
        '/economy/rewards/packages',
        '/economy/rewards/daily',
        '/economy/rewards/operations',
        '/economy/rewards/achievements',
      ])
  })

  it('keeps dynamic details and non-sidebar route variants in parent links', () => {
    const sidebarRoutes = new Set(navigationCatalog.groups.flatMap(group => group.children
      .filter(child => child.primary !== false)
      .map(child => child.routeName)))

    expect(navigationCatalog.routeParents.map(parent => parent.routeName)).toEqual(expect.arrayContaining([
      '/players/history/[crossplatformId]',
      '/players/profile/[crossplatformId]',
      '/community/chat/history',
      '/community/chat/mutes',
    ]))
    expect(navigationCatalog.routeParents.every(parent => !sidebarRoutes.has(parent.routeName))).toBe(true)
  })
})

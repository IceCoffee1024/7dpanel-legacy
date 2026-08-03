import type { RouteRecordRaw } from 'vue-router'

function redirectTo(path: string, destination: string): RouteRecordRaw {
  return {
    path,
    redirect: to => ({
      path: destination,
      query: to.query,
      hash: to.hash,
    }),
  }
}

export const navigationRedirects: RouteRecordRaw[] = [
  redirectTo('/operations', '/operations/server'),
  redirectTo('/community', '/community/chat/live'),
  redirectTo('/economy', '/economy/accounts'),
  redirectTo('/system', '/system/api-keys'),
  redirectTo('/backups', '/operations/backups'),
  redirectTo('/schedules', '/operations/automation/schedules'),
  redirectTo('/automation', '/operations/automation/rules'),
  redirectTo('/server-configuration', '/operations/configuration'),
  redirectTo('/mods', '/operations/extensions/mods'),
  redirectTo('/modules', '/operations/extensions/modules'),
  redirectTo('/world-tools', '/operations/world'),
  redirectTo('/console-logs', '/operations/console'),
  redirectTo('/game-resources', '/players/resources'),
  redirectTo('/access-lists', '/players/access-lists'),
  redirectTo('/game-chat/live', '/community/chat/live'),
  redirectTo('/game-chat/history', '/community/chat/history'),
  redirectTo('/game-chat/mutes', '/community/chat/mutes'),
  redirectTo('/game-chat/settings', '/community/chat/settings'),
  redirectTo('/game-chat/colored', '/community/chat/appearance'),
  redirectTo('/economy/reward-packages', '/economy/rewards/packages'),
  redirectTo('/economy/daily-reward', '/economy/rewards/daily'),
  redirectTo('/economy/reward-operations', '/economy/rewards/operations'),
  redirectTo('/economy/achievement-online-rewards', '/economy/rewards/achievements'),
  redirectTo('/economy/shop', '/economy/commerce/shop'),
  redirectTo('/economy/redeem-codes', '/economy/commerce/redeem-codes'),
  redirectTo('/permissions', '/system/access'),
  redirectTo('/api-keys', '/system/api-keys'),
  redirectTo('/integrations/discord', '/system/integrations/discord'),
  redirectTo('/integrations/geoip', '/system/integrations/geoip'),
  redirectTo('/audit', '/system/audit'),
]

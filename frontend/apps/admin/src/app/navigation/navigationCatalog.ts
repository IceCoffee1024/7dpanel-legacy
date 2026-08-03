import type { NavigationCatalog } from './navigationTypes'

export const navigationCatalog: NavigationCatalog = {
  groups: [
    {
      id: 'overview',
      labelKey: 'overview.title',
      icon: 'i-lucide-layout-dashboard',
      children: [
        { id: 'overview', routeName: '/', labelKey: 'overview.title', icon: 'i-lucide-layout-dashboard', searchable: true, shortcut: 'g-o' },
      ],
    },
    {
      id: 'operations',
      labelKey: 'shell.operations',
      icon: 'i-lucide-server-cog',
      children: [
        { id: 'server', routeName: '/operations/server', labelKey: 'serverOperations.title', icon: 'i-lucide-server', searchable: true, shortcut: 'g-s' },
        { id: 'backups', routeName: '/operations/backups', labelKey: 'backups.title', icon: 'i-lucide-database-backup', searchable: true },
        { id: 'schedules', routeName: '/operations/automation/schedules', labelKey: 'schedules.title', icon: 'i-lucide-calendar-clock', searchable: true },
        { id: 'automation', routeName: '/operations/automation/rules', labelKey: 'shell.automation', icon: 'i-lucide-workflow', searchable: true },
        { id: 'configuration', routeName: '/operations/configuration', labelKey: 'governance.serverConfiguration', icon: 'i-lucide-settings-2', searchable: true },
        { id: 'mods', routeName: '/operations/extensions/mods', labelKey: 'governance.mods', icon: 'i-lucide-blocks', searchable: true },
        { id: 'modules', routeName: '/operations/extensions/modules', labelKey: 'shell.modules', icon: 'i-lucide-boxes', searchable: true },
        { id: 'world', routeName: '/operations/world', labelKey: 'shell.worldTools', icon: 'i-lucide-hammer', searchable: true },
        { id: 'console', routeName: '/operations/console', labelKey: 'console.title', icon: 'i-lucide-terminal', searchable: true, shortcut: 'g-c' },
      ],
    },
    {
      id: 'players',
      labelKey: 'players.navigation',
      icon: 'i-lucide-users',
      children: [
        { id: 'online-players', routeName: '/players/', labelKey: 'players.navigation', icon: 'i-lucide-users', searchable: true, shortcut: 'g-p' },
        { id: 'player-history', routeName: '/players/history/', labelKey: 'players.profile.navigation', icon: 'i-lucide-contact-round', searchable: true },
        { id: 'player-map', routeName: '/players/map', labelKey: 'players.map.navigation', icon: 'i-lucide-map', searchable: true },
        { id: 'access-lists', routeName: '/players/access-lists', labelKey: 'governance.accessLists', icon: 'i-lucide-list-checks', searchable: true },
        { id: 'game-resources', routeName: '/players/resources', labelKey: 'gameResources.title', icon: 'i-lucide-package-search', searchable: true, shortcut: 'g-r' },
      ],
    },
    {
      id: 'community',
      labelKey: 'shell.community',
      icon: 'i-lucide-users-round',
      children: [
        { id: 'game-chat', routeName: '/community/chat/live', labelKey: 'gameChat.title', icon: 'i-lucide-messages-square', searchable: true, shortcut: 'g-g' },
        { id: 'teleport', routeName: '/community/teleport', labelKey: 'shell.teleportSettings', icon: 'i-lucide-map-pinned', searchable: true },
        { id: 'votes', routeName: '/community/votes', labelKey: 'shell.votes', icon: 'i-lucide-vote', searchable: true },
        { id: 'cities', routeName: '/community/cities', labelKey: 'shell.cities', icon: 'i-lucide-building-2', searchable: true },
      ],
    },
    {
      id: 'economy',
      labelKey: 'shell.economyAndRewards',
      icon: 'i-lucide-coins',
      children: [
        { id: 'economy-accounts', routeName: '/economy/accounts', labelKey: 'shell.economyAccounts', icon: 'i-lucide-wallet-cards', searchable: true },
        { id: 'economy-transactions', routeName: '/economy/transactions', labelKey: 'shell.economyTransactions', icon: 'i-lucide-receipt-text', searchable: true },
        { id: 'reward-packages', routeName: '/economy/rewards/packages', labelKey: 'shell.rewardPackages', icon: 'i-lucide-package-plus', searchable: true },
        { id: 'daily-reward', routeName: '/economy/rewards/daily', labelKey: 'shell.dailyReward', icon: 'i-lucide-calendar-check-2', searchable: true },
        { id: 'reward-operations', routeName: '/economy/rewards/operations', labelKey: 'shell.rewardOperations', icon: 'i-lucide-package-check', searchable: true },
        { id: 'achievements', routeName: '/economy/rewards/achievements', labelKey: 'shell.achievementsAndOnlineRewards', icon: 'i-lucide-trophy', searchable: true },
        { id: 'shop', routeName: '/economy/commerce/shop', labelKey: 'shell.shop', icon: 'i-lucide-store', searchable: true },
        { id: 'redeem-codes', routeName: '/economy/commerce/redeem-codes', labelKey: 'shell.redeemCodes', icon: 'i-lucide-ticket-check', searchable: true },
      ],
    },
    {
      id: 'system',
      labelKey: 'shell.systemManagement',
      icon: 'i-lucide-shield-check',
      children: [
        { id: 'permissions', routeName: '/system/access', labelKey: 'governance.permissions', icon: 'i-lucide-shield-check', searchable: true },
        { id: 'api-keys', routeName: '/system/api-keys', labelKey: 'apiKeys.title', icon: 'i-lucide-key-round', searchable: true, shortcut: 'g-k' },
        { id: 'discord', routeName: '/system/integrations/discord', labelKey: 'shell.discord', icon: 'i-lucide-message-square-share', searchable: true },
        { id: 'geoip', routeName: '/system/integrations/geoip', labelKey: 'shell.geoIp', icon: 'i-lucide-globe-lock', searchable: true },
        { id: 'audit', routeName: '/system/audit', labelKey: 'shell.auditAndEvents', icon: 'i-lucide-shield-ellipsis', searchable: true },
      ],
    },
  ],
  routeParents: [
    { routeName: '/players/history/[crossplatformId]', parentRouteName: '/players/history/', labelKey: 'players.profile.detail' },
    { routeName: '/players/profile/[crossplatformId]', parentRouteName: '/players/history/', labelKey: 'players.profile.detail' },
    { routeName: '/community/chat/history', parentRouteName: '/community/chat/live', labelKey: 'gameChat.history.title' },
    { routeName: '/community/chat/mutes', parentRouteName: '/community/chat/live', labelKey: 'shell.muteManagement' },
    { routeName: '/community/chat/settings', parentRouteName: '/community/chat/live', labelKey: 'gameChat.settings.title' },
    { routeName: '/community/chat/appearance', parentRouteName: '/community/chat/live', labelKey: 'gameChat.colored.title' },
  ],
}

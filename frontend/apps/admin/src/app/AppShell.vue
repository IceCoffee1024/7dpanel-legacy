<script setup lang="ts">
import type { DropdownMenuItem, NavigationMenuItem } from '@nuxt/ui'
import { defineShortcuts } from '@nuxt/ui/composables'
import { computed, shallowRef } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'

import { useAuthStore } from '../features/auth'

const router = useRouter()
const auth = useAuthStore()
const { t } = useI18n()
const sidebarOpen = shallowRef(false)
const canUseConsole = computed(() => auth.role === 'Owner' || auth.role === 'Admin')
const isOwner = computed(() => auth.role === 'Owner')

function closeSidebar() {
  sidebarOpen.value = false
}

const gameChatNavigation = computed<NavigationMenuItem[]>(() => [
  { label: t('gameChat.live.title'), icon: 'i-lucide-message-circle', to: '/game-chat/live', onSelect: closeSidebar },
  { label: t('gameChat.history.title'), icon: 'i-lucide-history', to: '/game-chat/history', onSelect: closeSidebar },
  { label: t('shell.muteManagement'), icon: 'i-lucide-volume-x', to: '/game-chat/mutes', onSelect: closeSidebar },
  { label: t('gameChat.settings.title'), icon: 'i-lucide-settings-2', to: '/game-chat/settings', onSelect: closeSidebar },
  { label: t('gameChat.colored.title'), icon: 'i-lucide-palette', to: '/game-chat/colored', onSelect: closeSidebar },
])

const playerAndWorldNavigation = computed<NavigationMenuItem[]>(() => [
  {
    label: t('players.navigation'),
    icon: 'i-lucide-users',
    to: '/players',
    onSelect: closeSidebar,
  },
  ...(isOwner.value
    ? [{
        label: t('players.profile.navigation'),
        icon: 'i-lucide-contact-round',
        to: '/players/history',
        onSelect: closeSidebar,
      }]
    : []),
  {
    label: t('gameResources.title'),
    icon: 'i-lucide-package-search',
    to: '/game-resources',
    onSelect: closeSidebar,
  },
])

const operationsNavigation = computed<NavigationMenuItem[]>(() => [
  { label: t('shell.worldTools'), icon: 'i-lucide-hammer', to: '/world-tools', onSelect: closeSidebar },
  { label: t('shell.modules'), icon: 'i-lucide-boxes', to: '/modules', onSelect: closeSidebar },
  { label: t('backups.title'), icon: 'i-lucide-database-backup', to: '/backups', onSelect: closeSidebar },
  { label: t('schedules.title'), icon: 'i-lucide-calendar-clock', to: '/schedules', onSelect: closeSidebar },
  { label: t('shell.automation'), icon: 'i-lucide-workflow', to: '/automation', onSelect: closeSidebar },
])

const economyNavigation = computed<NavigationMenuItem[]>(() => [
  { label: t('shell.economyAccounts'), icon: 'i-lucide-wallet-cards', to: '/economy/accounts', onSelect: closeSidebar },
  { label: t('shell.economyTransactions'), icon: 'i-lucide-receipt-text', to: '/economy/transactions', onSelect: closeSidebar },
  { label: t('shell.rewardPackages'), icon: 'i-lucide-package-plus', to: '/economy/reward-packages', onSelect: closeSidebar },
  { label: t('shell.dailyReward'), icon: 'i-lucide-calendar-check-2', to: '/economy/daily-reward', onSelect: closeSidebar },
  { label: t('shell.rewardOperations'), icon: 'i-lucide-package-check', to: '/economy/reward-operations', onSelect: closeSidebar },
  { label: t('shell.shop'), icon: 'i-lucide-store', to: '/economy/shop', onSelect: closeSidebar },
  { label: t('shell.redeemCodes'), icon: 'i-lucide-ticket-check', to: '/economy/redeem-codes', onSelect: closeSidebar },
  { label: t('shell.achievementsAndOnlineRewards'), icon: 'i-lucide-trophy', to: '/economy/achievement-online-rewards', onSelect: closeSidebar },
])

const communityNavigation = computed<NavigationMenuItem[]>(() => [
  { label: t('shell.teleportSettings'), icon: 'i-lucide-map-pinned', to: '/community/teleport', onSelect: closeSidebar },
  { label: t('shell.cities'), icon: 'i-lucide-building-2', to: '/community/cities', onSelect: closeSidebar },
  { label: t('shell.votes'), icon: 'i-lucide-vote', to: '/community/votes', onSelect: closeSidebar },
])

const integrationsNavigation = computed<NavigationMenuItem[]>(() => [
  { label: t('shell.discord'), icon: 'i-lucide-message-square-share', to: '/integrations/discord', onSelect: closeSidebar },
  { label: t('shell.geoIp'), icon: 'i-lucide-globe-lock', to: '/integrations/geoip', onSelect: closeSidebar },
])

const navigation = computed<NavigationMenuItem[]>(() => [
  {
    label: t('overview.title'),
    icon: 'i-lucide-layout-dashboard',
    to: '/',
    exact: true,
    onSelect: () => {
      sidebarOpen.value = false
    },
  },
  {
    label: t('shell.playersAndWorld'),
    icon: 'i-lucide-earth',
    children: playerAndWorldNavigation.value,
  },
  {
    label: 'API Keys',
    icon: 'i-lucide-key-round',
    to: '/api-keys',
    onSelect: () => {
      sidebarOpen.value = false
    },
  },
  ...(isOwner.value
    ? [{
        label: t('gameChat.title'),
        icon: 'i-lucide-messages-square',
        children: gameChatNavigation.value,
      }]
    : []),
  ...(isOwner.value
    ? [{
        label: t('shell.auditAndEvents'),
        icon: 'i-lucide-shield-ellipsis',
        to: '/audit',
        onSelect: closeSidebar,
      }]
    : []),
  ...(isOwner.value
    ? [{ label: t('shell.operations'), icon: 'i-lucide-server-cog', children: operationsNavigation.value }]
    : []),
  ...(isOwner.value
    ? [{ label: t('shell.economyAndRewards'), icon: 'i-lucide-coins', children: economyNavigation.value }]
    : []),
  ...(isOwner.value
    ? [{ label: t('shell.community'), icon: 'i-lucide-users-round', children: communityNavigation.value }]
    : []),
  ...(isOwner.value
    ? [{ label: t('shell.integrations'), icon: 'i-lucide-plug-zap', children: integrationsNavigation.value }]
    : []),
  ...(isOwner.value
    ? [{
        label: t('governance.serverConfiguration'),
        icon: 'i-lucide-settings-2',
        to: '/server-configuration',
        onSelect: () => { sidebarOpen.value = false },
      }]
    : []),
  {
    label: t('governance.accessLists'),
    icon: 'i-lucide-list-checks',
    to: '/access-lists',
    onSelect: () => { sidebarOpen.value = false },
  },
  ...(isOwner.value
    ? [{
        label: t('governance.permissions'),
        icon: 'i-lucide-shield-check',
        to: '/permissions',
        onSelect: () => { sidebarOpen.value = false },
      }]
    : []),
  {
    label: t('governance.mods'),
    icon: 'i-lucide-blocks',
    to: '/mods',
    onSelect: () => { sidebarOpen.value = false },
  },
  ...(canUseConsole.value
    ? [{
        label: t('console.title'),
        icon: 'i-lucide-terminal',
        to: '/console-logs',
        onSelect: () => {
          sidebarOpen.value = false
        },
      }]
    : []),
])

const searchGroups = computed(() => [{
  id: 'navigation',
  label: t('shell.navigation'),
  items: [
    {
      label: t('overview.title'),
      icon: 'i-lucide-layout-dashboard',
      to: '/',
    },
    ...playerAndWorldNavigation.value.map(({ label, icon, to }) => ({ label, icon, to })),
    {
      label: 'API Keys',
      icon: 'i-lucide-key-round',
      to: '/api-keys',
    },
    ...(isOwner.value
      ? gameChatNavigation.value.map(({ label, icon, to }) => ({ label, icon, to }))
      : []),
    ...(isOwner.value ? [{ label: t('shell.auditAndEvents'), icon: 'i-lucide-shield-ellipsis', to: '/audit' }] : []),
    ...(isOwner.value
      ? operationsNavigation.value.map(({ label, icon, to }) => ({ label, icon, to }))
      : []),
    ...(isOwner.value
      ? economyNavigation.value.map(({ label, icon, to }) => ({ label, icon, to }))
      : []),
    ...(isOwner.value
      ? communityNavigation.value.map(({ label, icon, to }) => ({ label, icon, to }))
      : []),
    ...(isOwner.value
      ? integrationsNavigation.value.map(({ label, icon, to }) => ({ label, icon, to }))
      : []),
    ...(isOwner.value ? [{ label: t('governance.serverConfiguration'), icon: 'i-lucide-settings-2', to: '/server-configuration' }] : []),
    { label: t('governance.accessLists'), icon: 'i-lucide-list-checks', to: '/access-lists' },
    ...(isOwner.value ? [{ label: t('governance.permissions'), icon: 'i-lucide-shield-check', to: '/permissions' }] : []),
    { label: t('governance.mods'), icon: 'i-lucide-blocks', to: '/mods' },
    ...(canUseConsole.value
      ? [{
          label: t('console.title'),
          icon: 'i-lucide-terminal',
          to: '/console-logs',
        }]
      : []),
  ],
}])

const accountName = computed(() => auth.username ?? '')
const accountRole = computed(() => auth.role ?? '')

async function logout() {
  auth.logout()
  await router.replace('/login')
}

const accountItems = computed<DropdownMenuItem[][]>(() => [[
  { label: accountName.value, type: 'label' },
  { label: accountRole.value, type: 'label' },
], [{
  label: t('shell.signOut'),
  icon: 'i-lucide-log-out',
  onSelect: logout,
}]])

defineShortcuts({
  'g-o': () => router.push('/'),
  'g-p': () => router.push('/players'),
  'g-r': () => router.push('/game-resources'),
  'g-k': () => router.push('/api-keys'),
  'g-c': () => router.push('/console-logs'),
  'g-g': () => {
    if (isOwner.value)
      void router.push('/game-chat/live')
  },
})
</script>

<template>
  <UDashboardGroup unit="rem" storage="local">
    <UDashboardSidebar
      id="default"
      v-model:open="sidebarOpen"
      collapsible
      resizable
      class="bg-elevated/25"
      :ui="{ footer: 'lg:border-t lg:border-default' }"
    >
      <template #header="{ collapsed }">
        <AppBrand :collapsed="collapsed" />
      </template>

      <template #default="{ collapsed }">
        <UDashboardSearchButton
          :collapsed="collapsed"
          :label="t('shell.search')"
          class="bg-transparent ring-default"
        />

        <UNavigationMenu
          :collapsed="collapsed"
          :items="navigation"
          orientation="vertical"
          tooltip
          popover
        />
      </template>

      <template #footer="{ collapsed }">
        <div class="flex flex-col gap-1">
          <AppearanceMenu :collapsed="collapsed" />
          <LocaleMenu :collapsed="collapsed" />

          <UDropdownMenu
            :items="accountItems"
            :content="{ align: 'center', collisionPadding: 12 }"
            :ui="{ content: collapsed ? 'w-40' : 'w-(--reka-dropdown-menu-trigger-width)' }"
          >
            <UButton
              :aria-label="t('shell.account', { name: accountName })"
              block
              class="min-w-0"
              color="neutral"
              data-testid="account-menu-trigger"
              icon="i-lucide-circle-user-round"
              :label="accountName"
              :square="collapsed"
              :trailing-icon="collapsed ? undefined : 'i-lucide-chevrons-up-down'"
              variant="ghost"
            />
          </UDropdownMenu>
        </div>
      </template>
    </UDashboardSidebar>

    <UDashboardSearch
      :title="t('shell.search')"
      :placeholder="t('shell.searchPages')"
      :groups="searchGroups"
    />

    <RouterView />
  </UDashboardGroup>
</template>

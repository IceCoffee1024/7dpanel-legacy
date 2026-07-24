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
    label: t('players.navigation'),
    icon: 'i-lucide-users',
    to: '/players',
    onSelect: () => {
      sidebarOpen.value = false
    },
  },
  {
    label: 'API Keys',
    icon: 'i-lucide-key-round',
    to: '/api-keys',
    onSelect: () => {
      sidebarOpen.value = false
    },
  },
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
    {
      label: t('players.navigation'),
      icon: 'i-lucide-users',
      to: '/players',
    },
    {
      label: 'API Keys',
      icon: 'i-lucide-key-round',
      to: '/api-keys',
    },
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
  'g-k': () => router.push('/api-keys'),
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

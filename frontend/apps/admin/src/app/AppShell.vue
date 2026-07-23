<script setup lang="ts">
import type { DropdownMenuItem, NavigationMenuItem } from '@nuxt/ui'
import { computed, shallowRef } from 'vue'
import { useRouter } from 'vue-router'

import { useAuthStore } from '../features/auth'

const router = useRouter()
const auth = useAuthStore()
const sidebarOpen = shallowRef(false)

const navigation = [
  {
    label: '概览',
    icon: 'i-lucide-layout-dashboard',
    to: '/',
    exact: true,
    onSelect: () => {
      sidebarOpen.value = false
    },
  },
  {
    label: '玩家',
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
] satisfies NavigationMenuItem[]

const searchGroups = [{
  id: 'navigation',
  label: '导航',
  items: navigation,
}]

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
  label: '退出登录',
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
          label="搜索"
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

          <UDropdownMenu
            :items="accountItems"
            :content="{ align: 'center', collisionPadding: 12 }"
            :ui="{ content: collapsed ? 'w-40' : 'w-(--reka-dropdown-menu-trigger-width)' }"
          >
            <UButton
              :aria-label="`${accountName} 账号`"
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
      title="搜索"
      placeholder="搜索页面"
      :groups="searchGroups"
    />

    <RouterView />
  </UDashboardGroup>
</template>

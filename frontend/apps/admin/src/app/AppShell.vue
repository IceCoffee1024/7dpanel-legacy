<script setup lang="ts">
import type { DropdownMenuItem } from '@nuxt/ui'
import type { NavigationGroupId, NavigationRouteName } from './navigation/navigationTypes'

import { defineShortcuts } from '@nuxt/ui/composables'
import { computed, shallowRef, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'

import AppBreadcrumbs from '../components/navigation/AppBreadcrumbs.vue'
import PrimaryNavigation from '../components/navigation/PrimaryNavigation.vue'
import SecondaryNavigation from '../components/navigation/SecondaryNavigation.vue'
import { useAuthStore } from '../features/auth'
import { useNavigation } from './navigation/useNavigation'

const router = useRouter()
const auth = useAuthStore()
const { t } = useI18n()
const sidebarOpen = shallowRef(false)
const searchOpen = shallowRef(false)
const selectedGroupId = shallowRef<NavigationGroupId | undefined>()
const navigation = useNavigation()

watch(() => navigation.activeGroupId.value, (groupId) => {
  selectedGroupId.value = groupId
}, { immediate: true })

const selectedGroup = computed(() => navigation.groups.value.find(group => group.id === selectedGroupId.value)
  ?? navigation.currentGroup.value)

function closeSidebar() {
  sidebarOpen.value = false
}

function selectGroup(groupId: NavigationGroupId) {
  selectedGroupId.value = groupId
}

async function navigate(routeName: NavigationRouteName) {
  await router.push(routeLocation(routeName))
  closeSidebar()
}

function routeLocation(routeName: NavigationRouteName) {
  return { name: routeName } as never
}

function createSearchItem(item: typeof navigation.searchItems.value[number]) {
  return {
    label: t(item.labelKey),
    icon: item.icon,
    to: router.resolve(routeLocation(item.routeName)).href,
    async onSelect(event: Event) {
      event.preventDefault()
      await navigate(item.routeName)
      searchOpen.value = false
    },
  }
}

const searchGroups = computed(() => [{
  id: 'navigation',
  label: t('shell.navigation'),
  items: navigation.searchItems.value.map(createSearchItem),
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

const shortcutHandlers = computed(() => Object.fromEntries(
  navigation.shortcuts.value.map(shortcut => [
    shortcut.shortcut,
    () => router.push(routeLocation(shortcut.routeName)),
  ]),
))

defineShortcuts(shortcutHandlers.value)
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
          @click="searchOpen = true"
        />
        <PrimaryNavigation
          :active-group-id="selectedGroup?.id"
          :collapsed="collapsed"
          :groups="navigation.groups.value"
          @select="selectGroup"
        />
        <SecondaryNavigation
          :active-route-name="navigation.breadcrumbs.value[navigation.breadcrumbs.value.length - 1]?.routeName"
          :collapsed="collapsed"
          :items="selectedGroup?.children ?? []"
          @select="navigate"
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
      v-model:open="searchOpen"
      :description="t('dashboardSearch.description')"
      :title="t('shell.search')"
      :placeholder="t('shell.searchPages')"
      :groups="searchGroups"
    />

    <div class="contents">
      <AppBreadcrumbs :items="navigation.breadcrumbs.value" @select="navigate" />
      <RouterView />
    </div>
  </UDashboardGroup>
</template>

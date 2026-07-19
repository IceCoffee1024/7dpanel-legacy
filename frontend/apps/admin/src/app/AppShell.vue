<script setup lang="ts">
import { shallowRef } from 'vue'
import { useRouter } from 'vue-router'
import type { NavigationMenuItem } from '@nuxt/ui'

const router = useRouter()
const sidebarOpen = shallowRef(false)

const navigation = [{
  label: '概览',
  icon: 'i-lucide-layout-dashboard',
  to: '/',
  exact: true,
  onSelect: () => {
    sidebarOpen.value = false
  }
}] satisfies NavigationMenuItem[]

const searchGroups = [{
  id: 'navigation',
  label: '导航',
  items: navigation
}]

defineShortcuts({
  'g-o': () => router.push('/')
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
        <AppearanceMenu :collapsed="collapsed" />
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

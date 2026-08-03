<route lang="json">
{
  "meta": {
    "requiresAuth": true,
    "roles": ["Owner"]
  }
}
</route>

<script setup lang="ts">
import ServerOperationsView from '../../features/server-operations/ui/ServerOperationsView.vue'
</script>

<template>
  <UDashboardPanel id="server-operations">
    <template #header>
      <UDashboardNavbar :title="$t('serverOperations.title')">
        <template #leading>
          <UDashboardSidebarCollapse />
        </template>
      </UDashboardNavbar>
    </template>
    <template #body>
      <ServerOperationsView />
    </template>
  </UDashboardPanel>
</template>

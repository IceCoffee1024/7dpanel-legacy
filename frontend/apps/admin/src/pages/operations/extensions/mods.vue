<route lang="json">
{
  "meta": {
    "requiresAuth": true
  }
}
</route>

<script setup lang="ts">
import NavigationSectionTabs from '../../../components/navigation/NavigationSectionTabs.vue'
import { ModsView } from '../../../features/mods'
</script>

<template>
  <NavigationSectionTabs />
  <ModsView />
</template>

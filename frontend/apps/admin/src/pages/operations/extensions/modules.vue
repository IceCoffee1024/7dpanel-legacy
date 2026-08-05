<route lang="json">
{
  "meta": {
    "requiresAuth": true,
    "roles": ["Owner"]
  }
}
</route>

<script setup lang="ts">
import NavigationSectionTabs from '../../../components/navigation/NavigationSectionTabs.vue'
import { FeatureModulesView } from '../../../features/modules'
</script>

<template>
  <NavigationSectionTabs />
  <FeatureModulesView />
</template>

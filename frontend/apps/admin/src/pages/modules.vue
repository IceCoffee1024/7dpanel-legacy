<route lang="json">
{
  "meta": {
    "requiresAuth": true,
    "roles": ["Owner"]
  }
}
</route>

<script setup lang="ts">
import { FeatureModulesView } from '../features/modules'
</script>

<template>
  <FeatureModulesView />
</template>

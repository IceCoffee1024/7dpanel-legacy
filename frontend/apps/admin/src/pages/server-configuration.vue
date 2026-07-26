<route lang="json">
{
  "meta": {
    "requiresAuth": true,
    "roles": ["Owner"]
  }
}
</route>

<script setup lang="ts">
import { ServerConfigurationView } from '../features/server-configuration'
</script>

<template>
  <ServerConfigurationView />
</template>

<route lang="json">
{
  "meta": {
    "requiresAuth": true,
    "roles": ["Owner"]
  }
}
</route>

<script setup lang="ts">
import { WorldToolsView } from '../../features/world-tools'
</script>

<template>
  <WorldToolsView />
</template>

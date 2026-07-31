<route lang="json">
{ "meta": { "requiresAuth": true, "roles": ["Owner"] } }
</route>

<script setup lang="ts">
import HistoricalPlayerView from '../../../features/players/ui/HistoricalPlayerView.vue'
</script>

<template>
  <HistoricalPlayerView />
</template>

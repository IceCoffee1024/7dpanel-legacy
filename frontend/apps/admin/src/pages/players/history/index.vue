<route lang="json">
{ "meta": { "requiresAuth": true, "roles": ["Owner"] } }
</route>

<script setup lang="ts">
import HistoricalPlayersView from '../../../features/players/ui/HistoricalPlayersView.vue'
</script>

<template>
  <HistoricalPlayersView />
</template>

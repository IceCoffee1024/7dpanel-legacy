<route lang="json">
{ "meta": { "requiresAuth": true, "roles": ["Owner"] } }
</route>

<script setup lang="ts">
import PlayerMapView from '../../features/player-map/ui/PlayerMapView.vue'
</script>

<template>
  <PlayerMapView />
</template>

<route lang="json">
{
  "meta": {
    "requiresAuth": true
  }
}
</route>

<script setup lang="ts">
import {
  GameResourcesView,
  generatedGameResourcesLoader,
} from '../../features/game-resources'
</script>

<template>
  <GameResourcesView :load="generatedGameResourcesLoader" />
</template>

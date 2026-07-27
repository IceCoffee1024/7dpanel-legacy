<route lang="json">
{
  "meta": {
    "requiresAuth": true,
    "roles": ["Owner"]
  }
}
</route>

<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'

import PlayerProfileView from '../../../features/player-profile/ui/PlayerProfileView.vue'

const route = useRoute()
const crossplatformId = computed(() => {
  const value = route.params.crossplatformId
  return typeof value === 'string' ? value : ''
})
</script>

<template>
  <PlayerProfileView :crossplatform-id="crossplatformId" />
</template>

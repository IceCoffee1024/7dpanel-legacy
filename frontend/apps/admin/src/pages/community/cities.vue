<route lang="json">
{
  "meta": {
    "requiresAuth": true,
    "roles": ["Owner"]
  }
}
</route>

<script setup lang="ts">
import type { CityInput } from '../../features/community'

import { onMounted, onUnmounted } from 'vue'

import { CitiesView, useCommunity } from '../../features/community'

const controller = useCommunity()

function refresh() {
  void Promise.all([
    controller.loadCities(),
    controller.loadAllCities(),
  ])
}

async function save(input: CityInput) {
  if (await controller.saveCity(input))
    await controller.loadAllCities()
}

onMounted(refresh)
onUnmounted(controller.dispose)
</script>

<template>
  <CitiesView
    :controller="controller"
    @dismiss-mutation="controller.clearMutationState"
    @refresh="refresh"
    @save="save"
  />
</template>

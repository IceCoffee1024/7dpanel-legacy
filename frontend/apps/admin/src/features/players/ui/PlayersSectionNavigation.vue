<script setup lang="ts">
import { computed } from 'vue'

import { navigationCatalog } from '../../../app/navigation/navigationCatalog'
import SectionTabs from '../../../components/navigation/SectionTabs.vue'

const items = computed(() => navigationCatalog.groups
  .find(group => group.id === 'players')
  ?.children
  .filter(entry => entry.id === 'online-players' || entry.id === 'player-history' || entry.id === 'player-map')
  .map(entry => Object.freeze({ ...entry, groupId: 'players' as const })) ?? [])
</script>

<template>
  <SectionTabs :items="items" />
</template>

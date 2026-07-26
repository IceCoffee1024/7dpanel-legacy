<script setup lang="ts">
import type { PlayerTrackSegment } from '../api/playerMap'

import { computed } from 'vue'
import { useI18n } from 'vue-i18n'

const props = defineProps<{
  segments: readonly PlayerTrackSegment[]
  selectedSnapshotId: number | null
}>()

const emit = defineEmits<{
  select: [snapshotId: number]
}>()

const { d, t } = useI18n()
const observations = computed(() => props.segments.flatMap(segment => segment.points))
</script>

<template>
  <section aria-labelledby="track-observations-title" class="min-w-0 space-y-2">
    <h2 id="track-observations-title" class="text-sm font-semibold">
      {{ t('players.map.observations') }}
    </h2>
    <p v-if="observations.length === 0" class="text-sm text-muted">
      {{ t('players.map.noObservations') }}
    </p>
    <ol v-else class="max-h-72 space-y-1 overflow-y-auto" data-testid="track-observations">
      <li v-for="observation in observations" :key="observation.snapshotId">
        <button
          class="w-full rounded-md border px-3 py-2 text-left text-sm focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary"
          :class="observation.snapshotId === selectedSnapshotId ? 'border-primary bg-primary/10' : 'border-default bg-default'"
          type="button"
          @click="emit('select', observation.snapshotId)"
        >
          <span class="block font-medium">{{ observation.name }}</span>
          <span class="block text-muted">{{ d(new Date(observation.observedAtUtc), 'playerObservation') }}</span>
          <span class="block font-mono text-xs">X {{ observation.x }} · Y {{ observation.y }} · Z {{ observation.z }}</span>
        </button>
      </li>
    </ol>
  </section>
</template>

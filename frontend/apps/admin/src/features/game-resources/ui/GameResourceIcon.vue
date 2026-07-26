<script setup lang="ts">
import type { GameResourceIconStatus } from '../api/gameResources'

import { computed, useTemplateRef } from 'vue'

import { useAuthStore } from '../../auth'
import { useGameResourceIcon } from '../model/useGameResourceIcon'

const props = defineProps<{
  resourceId: string
  iconStatus: GameResourceIconStatus
  alt: string
}>()

const auth = useAuthStore()
const root = useTemplateRef<HTMLElement>('root')
const authorizationHeader = computed(() => auth.authorizationHeader)
const { src } = useGameResourceIcon({
  resourceId: () => props.resourceId,
  iconStatus: () => props.iconStatus,
  authorizationHeader,
  target: root,
})
</script>

<template>
  <span
    ref="root"
    class="inline-flex size-10 shrink-0 items-center justify-center overflow-hidden rounded-md border border-default bg-elevated"
  >
    <img
      v-if="src"
      :alt="alt"
      class="size-full object-contain"
      decoding="async"
      :src="src"
    >
    <span
      v-else
      :aria-label="alt"
      class="inline-flex size-full items-center justify-center text-dimmed"
      data-testid="game-resource-icon-placeholder"
      role="img"
    >
      <UIcon name="i-lucide-package-open" class="size-5" />
    </span>
  </span>
</template>

<script setup lang="ts">
import type { OnlinePlayersErrorCode, OnlinePlayersState } from '../model/useOnlinePlayers'

import { computed } from 'vue'
import { useI18n } from 'vue-i18n'

type DisplayState = OnlinePlayersState | 'empty'

const props = withDefaults(defineProps<{
  state: DisplayState
  errorCode?: OnlinePlayersErrorCode
}>(), {
  errorCode: null,
})

defineEmits<{
  refresh: []
}>()

const { t } = useI18n()

const content = computed(() => {
  if (props.state === 'empty') {
    return {
      icon: 'i-lucide-users',
      title: t('players.state.emptyTitle'),
      description: '',
    }
  }
  if (props.state === 'forbidden') {
    return {
      icon: 'i-lucide-shield-alert',
      title: t('players.state.forbiddenTitle'),
      description: t('players.state.forbiddenDescription'),
    }
  }
  if (props.errorCode === 'game-not-ready') {
    return {
      icon: 'i-lucide-loader-circle',
      title: t('players.state.notReadyTitle'),
      description: t('players.state.notReadyDescription'),
    }
  }
  return {
    icon: 'i-lucide-wifi-off',
    title: t('players.state.offlineTitle'),
    description: t('players.state.offlineDescription'),
  }
})
</script>

<template>
  <div
    v-if="state === 'loading'"
    :aria-label="t('players.state.loading')"
    class="space-y-3"
    data-testid="players-loading"
  >
    <USkeleton v-for="row in 5" :key="row" class="h-14 w-full" />
  </div>

  <section
    v-else
    :data-testid="state === 'empty' ? 'players-empty' : `players-${state}`"
    class="mx-auto flex min-h-72 max-w-md flex-col items-center justify-center py-12 text-center"
  >
    <span class="mb-4 flex size-11 items-center justify-center rounded-md bg-elevated text-muted">
      <UIcon :name="content.icon" class="size-5" />
    </span>
    <h2 class="text-base font-semibold text-highlighted">
      {{ content.title }}
    </h2>
    <p v-if="content.description" class="mt-2 text-sm text-muted">
      {{ content.description }}
    </p>
    <div v-if="state === 'forbidden'" class="mt-6">
      <UButton
        color="neutral"
        icon="i-lucide-arrow-left"
        :label="t('common.backToOverview')"
        to="/"
        variant="outline"
      />
    </div>
    <UButton
      v-else-if="state === 'offline'"
      class="mt-6"
      color="neutral"
      icon="i-lucide-refresh-cw"
      :label="t('common.reload')"
      variant="outline"
      @click="$emit('refresh')"
    />
  </section>
</template>

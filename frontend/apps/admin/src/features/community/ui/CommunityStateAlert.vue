<script setup lang="ts">
import type { CommunityViewState } from '../model/useCommunity'

import { useI18n } from 'vue-i18n'

defineProps<{
  state: CommunityViewState
  subject: string
}>()

defineEmits<{ retry: [] }>()
const { t } = useI18n()
</script>

<template>
  <UAlert
    v-if="state === 'forbidden'"
    color="warning"
    icon="i-lucide-shield-alert"
    :title="t('community.state.forbiddenTitle', { subject })"
    :description="t('community.state.forbiddenDescription')"
  />
  <UAlert
    v-else-if="state === 'unavailable'"
    color="error"
    icon="i-lucide-circle-x"
    :title="t('community.state.unavailableTitle', { subject })"
    :description="t('community.state.unavailableDescription')"
  >
    <template #actions>
      <UButton
        color="neutral"
        :label="t('community.common.retry')"
        variant="outline"
        @click="$emit('retry')"
      />
    </template>
  </UAlert>
  <UAlert
    v-else-if="state === 'stale'"
    color="warning"
    icon="i-lucide-refresh-cw-off"
    :title="t('community.state.staleTitle', { subject })"
    :description="t('community.state.staleDescription')"
  >
    <template #actions>
      <UButton
        color="neutral"
        :label="t('community.common.retry')"
        variant="outline"
        @click="$emit('retry')"
      />
    </template>
  </UAlert>
</template>

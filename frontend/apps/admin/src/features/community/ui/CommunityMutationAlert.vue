<script setup lang="ts">
import type { CommunityMutationState } from '../model/useCommunity'

import { useI18n } from 'vue-i18n'

defineProps<{ state: CommunityMutationState }>()
defineEmits<{ dismiss: [] }>()
const { t } = useI18n()
</script>

<template>
  <UAlert
    v-if="state === 'confirmed'"
    color="success"
    icon="i-lucide-circle-check"
    :title="t('community.mutation.confirmedTitle')"
    :description="t('community.mutation.confirmedDescription')"
    :close="true"
    @update:open="$emit('dismiss')"
  />
  <UAlert
    v-else-if="state === 'forbidden'"
    color="warning"
    icon="i-lucide-shield-alert"
    :title="t('community.mutation.forbiddenTitle')"
    :description="t('community.mutation.forbiddenDescription')"
    :close="true"
    @update:open="$emit('dismiss')"
  />
  <UAlert
    v-else-if="state === 'unavailable'"
    color="warning"
    icon="i-lucide-triangle-alert"
    :title="t('community.mutation.unavailableTitle')"
    :description="t('community.mutation.unavailableDescription')"
    :close="true"
    @update:open="$emit('dismiss')"
  />
  <UAlert
    v-else-if="state === 'failed'"
    color="error"
    icon="i-lucide-circle-x"
    :title="t('community.mutation.failedTitle')"
    :description="t('community.mutation.failedDescription')"
    :close="true"
    @update:open="$emit('dismiss')"
  />
</template>

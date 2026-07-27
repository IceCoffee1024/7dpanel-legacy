<script setup lang="ts">
import type { EvidenceLevel, ProfileSectionState } from './playerProfileUi'

import { computed } from 'vue'
import { useI18n } from 'vue-i18n'

const props = defineProps<{
  level?: EvidenceLevel
  state?: ProfileSectionState
  gap?: boolean
}>()
const { t } = useI18n()
const label = computed(() => {
  if (props.gap)
    return t('players.profile.evidence.gap')
  if (props.level === 'Confirmed')
    return t('players.profile.evidence.confirmed')
  if (props.level === 'ObservedChange')
    return t('players.profile.evidence.observedChange')
  return t(`players.profile.section.${(props.state ?? 'Unavailable').toLowerCase()}`)
})
const color = computed(() => props.gap || props.state === 'Partial'
  ? 'warning'
  : props.level === 'Confirmed' || props.state === 'Available' ? 'success' : 'neutral')
</script>

<template>
  <UBadge :color="color" size="sm" variant="subtle">
    {{ label }}
  </UBadge>
</template>

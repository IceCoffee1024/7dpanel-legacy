<script setup lang="ts">
import type { NavigationGroupId, NavigationGroupProjection } from '../../app/navigation/navigationTypes'

import { computed } from 'vue'
import { useI18n } from 'vue-i18n'

const props = defineProps<{
  groups: readonly NavigationGroupProjection[]
  activeGroupId?: NavigationGroupId
  collapsed?: boolean
}>()

const emit = defineEmits<{
  select: [groupId: NavigationGroupId]
}>()

const { t } = useI18n()
const items = computed(() => props.groups.map(group => ({ ...group, label: t(group.labelKey) })))
</script>

<template>
  <nav :aria-label="t('shell.primaryNavigation')" class="flex flex-col gap-1" data-testid="primary-navigation">
    <UButton
      v-for="group in items"
      :key="group.id"
      block
      :aria-current="group.id === activeGroupId ? 'page' : undefined"
      :aria-expanded="group.id === activeGroupId"
      :color="group.id === activeGroupId ? 'primary' : 'neutral'"
      :icon="group.icon"
      :label="collapsed ? undefined : group.label"
      :square="collapsed"
      :title="collapsed ? group.label : undefined"
      :variant="group.id === activeGroupId ? 'soft' : 'ghost'"
      @click="emit('select', group.id)"
    />
  </nav>
</template>

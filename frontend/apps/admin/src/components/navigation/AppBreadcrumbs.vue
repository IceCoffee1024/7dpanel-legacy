<script setup lang="ts">
import type { NavigationBreadcrumb, NavigationRouteName } from '../../app/navigation/navigationTypes'

import { useI18n } from 'vue-i18n'

defineProps<{
  items: readonly NavigationBreadcrumb[]
}>()

const emit = defineEmits<{
  select: [routeName: NavigationRouteName]
}>()

const { t } = useI18n()
</script>

<template>
  <nav
    v-if="items.length > 0"
    :aria-label="t('shell.breadcrumbs')"
    class="px-4 pt-3 lg:px-6"
    data-testid="app-breadcrumbs"
  >
    <ol class="flex min-w-0 flex-wrap items-center gap-1 text-sm text-muted">
      <li v-for="(item, index) in items" :key="`${item.routeName}-${index}`" class="flex min-w-0 items-center gap-1">
        <span v-if="index > 0" aria-hidden="true">/</span>
        <button
          v-if="index < items.length - 1"
          class="truncate text-left hover:text-highlighted"
          type="button"
          @click="emit('select', item.routeName)"
        >
          {{ t(item.labelKey) }}
        </button>
        <span v-else aria-current="page" class="truncate">{{ t(item.labelKey) }}</span>
      </li>
    </ol>
  </nav>
</template>

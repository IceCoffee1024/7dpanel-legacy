<script setup lang="ts">
import type { RouteLocationRaw } from 'vue-router'
import type { NavigationEntryProjection } from '../../app/navigation/navigationTypes'

import { useI18n } from 'vue-i18n'

defineProps<{
  items: readonly NavigationEntryProjection[]
}>()

const { t } = useI18n()

function toRoute(item: NavigationEntryProjection): RouteLocationRaw {
  return { name: item.routeName } as unknown as RouteLocationRaw
}
</script>

<template>
  <nav class="border-b border-default" :aria-label="t('shell.navigation')" data-testid="section-tabs">
    <ul class="flex gap-1 overflow-x-auto" role="list">
      <li v-for="item in items" :key="item.id" class="shrink-0">
        <RouterLink
          v-slot="{ href, isExactActive, navigate }"
          :to="toRoute(item)"
          custom
        >
          <a
            :href="href"
            class="flex min-h-9 items-center gap-2 border-b-2 px-3 py-2 text-sm font-medium focus-visible:outline-2 focus-visible:outline-offset-[-2px] focus-visible:outline-primary"
            :class="isExactActive ? 'border-primary text-highlighted' : 'border-transparent text-muted hover:text-highlighted'"
            :aria-current="isExactActive ? 'page' : undefined"
            @click="navigate"
          >
            <UIcon :name="item.icon" class="size-4" />
            <span>{{ t(item.labelKey) }}</span>
          </a>
        </RouterLink>
      </li>
    </ul>
  </nav>
</template>

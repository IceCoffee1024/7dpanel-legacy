<script setup lang="ts">
import type { RouteLocationRaw } from 'vue-router'
import type { NavigationEntryProjection, NavigationRouteName } from '../../app/navigation/navigationTypes'

import { computed } from 'vue'
import { useI18n } from 'vue-i18n'

const props = defineProps<{
  items: readonly NavigationEntryProjection[]
  activeRouteName?: NavigationRouteName
  collapsed?: boolean
}>()

const emit = defineEmits<{
  select: [routeName: NavigationRouteName]
}>()

const { t } = useI18n()
const navigationItems = computed(() => props.items.map(item => ({ ...item, label: t(item.labelKey) })))

function routeTarget(routeName: NavigationRouteName): RouteLocationRaw {
  return { name: routeName } as never
}
</script>

<template>
  <nav
    v-if="navigationItems.length > 0"
    :aria-label="t('shell.navigation')"
    class="mt-2 border-t border-default pt-2"
    data-testid="secondary-navigation"
  >
    <RouterLink
      v-for="item in navigationItems"
      :key="item.id"
      v-slot="{ href }"
      :to="routeTarget(item.routeName)"
      custom
    >
      <a
        :href="href"
        class="mb-1 flex min-h-9 items-center gap-2 rounded-md px-3 py-2 text-sm font-medium"
        :class="item.routeName === activeRouteName ? 'bg-primary/10 text-highlighted' : 'text-muted hover:bg-elevated hover:text-highlighted'"
        :aria-current="item.routeName === activeRouteName ? 'page' : undefined"
        :title="collapsed ? item.label : undefined"
        @click="(event) => { event.preventDefault(); emit('select', item.routeName) }"
      >
        <UIcon :name="item.icon" class="size-4 shrink-0" />
        <span v-if="!collapsed" class="truncate">{{ item.label }}</span>
      </a>
    </RouterLink>
  </nav>
</template>

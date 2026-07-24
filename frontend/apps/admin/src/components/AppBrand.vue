<script setup lang="ts">
import { useI18n } from 'vue-i18n'

defineProps<{
  collapsed?: boolean
}>()

const { t } = useI18n()
</script>

<template>
  <RouterLink
    to="/"
    class="flex min-h-9 items-center gap-3 rounded-md px-1.5 text-highlighted focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary"
    :aria-label="t('overview.brandLabel')"
  >
    <span class="flex size-8 shrink-0 items-center justify-center rounded-md bg-primary text-inverted">
      <UIcon name="i-lucide-server-cog" class="size-5" />
    </span>

    <span v-if="!collapsed" class="min-w-0">
      <span class="block truncate text-sm font-semibold">7DPanel</span>
      <span class="block truncate text-xs text-muted">{{ t('auth.brandDescription') }}</span>
    </span>
  </RouterLink>
</template>

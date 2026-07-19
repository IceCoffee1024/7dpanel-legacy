<script setup lang="ts">
defineProps<{
  collapsed?: boolean
}>()
</script>

<template>
  <RouterLink
    to="/"
    class="flex min-h-9 items-center gap-3 rounded-md px-1.5 text-highlighted focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary"
    aria-label="7DPanel 概览"
  >
    <span class="flex size-8 shrink-0 items-center justify-center rounded-md bg-primary text-inverted">
      <UIcon name="i-lucide-server-cog" class="size-5" />
    </span>

    <span v-if="!collapsed" class="min-w-0">
      <span class="block truncate text-sm font-semibold">7DPanel</span>
      <span class="block truncate text-xs text-muted">服务器管理</span>
    </span>
  </RouterLink>
</template>

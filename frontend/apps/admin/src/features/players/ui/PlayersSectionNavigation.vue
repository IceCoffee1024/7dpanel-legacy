<script setup lang="ts">
import { useI18n } from 'vue-i18n'

const { t } = useI18n()
</script>

<template>
  <nav :aria-label="t('players.history.sectionNavigation')" class="flex gap-2">
    <UButton
      color="neutral"
      icon="i-lucide-users"
      :label="t('players.history.online')"
      to="/players"
      variant="ghost"
    />
    <UButton
      color="neutral"
      icon="i-lucide-history"
      :label="t('players.history.navigation')"
      to="/players/history"
      variant="ghost"
    />
    <UButton
      color="neutral"
      icon="i-lucide-map"
      :label="t('players.map.navigation')"
      to="/players/map"
      variant="ghost"
    />
  </nav>
</template>

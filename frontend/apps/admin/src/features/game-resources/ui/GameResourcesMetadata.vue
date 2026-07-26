<script setup lang="ts">
import type { GameResourcePage } from '../api/gameResources'

import { useI18n } from 'vue-i18n'

defineProps<{
  page: GameResourcePage
}>()

const { d, t } = useI18n()
</script>

<template>
  <dl class="grid gap-3 rounded-md border border-default bg-elevated/25 p-4 text-sm sm:grid-cols-2 xl:grid-cols-4">
    <div>
      <dt class="text-muted">
        {{ t('gameResources.metadata.catalogVersion') }}
      </dt>
      <dd class="mt-1 break-all font-medium text-highlighted">
        {{ page.catalogVersion }}
      </dd>
    </div>
    <div>
      <dt class="text-muted">
        {{ t('gameResources.metadata.gameVersion') }}
      </dt>
      <dd class="mt-1 font-medium text-highlighted">
        {{ page.gameVersion ?? t('gameResources.values.unavailable') }}
      </dd>
    </div>
    <div>
      <dt class="text-muted">
        {{ t('gameResources.metadata.observedAt') }}
      </dt>
      <dd class="mt-1 font-medium text-highlighted">
        {{ d(new Date(page.observedAtUtc), 'medium') }}
      </dd>
    </div>
    <div>
      <dt class="text-muted">
        {{ t('gameResources.metadata.page') }}
      </dt>
      <dd class="mt-1 font-medium text-highlighted">
        {{ t('gameResources.metadata.pageValue', { page: page.page, pageSize: page.pageSize }) }}
      </dd>
    </div>
  </dl>
</template>

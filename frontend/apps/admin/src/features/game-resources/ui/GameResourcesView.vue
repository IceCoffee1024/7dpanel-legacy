<script setup lang="ts">
import type { LoadGameResources } from '../api/gameResources'

import { useToast } from '@nuxt/ui/composables'
import { computed, inject } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'

import { useAuthStore } from '../../auth'
import { useGameResources } from '../model/useGameResources'
import { GAME_RESOURCES_LOADER_KEY, unavailableGameResourcesLoader } from '../transport'

import GameResourcesFilters from './GameResourcesFilters.vue'
import GameResourcesList from './GameResourcesList.vue'
import GameResourcesMetadata from './GameResourcesMetadata.vue'
import GameResourcesStatus from './GameResourcesStatus.vue'
import GameResourcesTable from './GameResourcesTable.vue'

const props = defineProps<{
  load?: LoadGameResources
}>()

const auth = useAuthStore()
const route = useRoute()
const router = useRouter()
const toast = useToast()
const { locale, t } = useI18n()
const injectedLoader = inject(GAME_RESOURCES_LOADER_KEY, null)
const load = props.load ?? injectedLoader ?? unavailableGameResourcesLoader
const isOwner = computed(() => auth.role === 'Owner')

function handleSessionExpired() {
  auth.expireSession()
  void router.replace({
    path: '/login',
    query: { redirect: route.fullPath },
  })
}

const resources = useGameResources({
  load,
  locale,
  isOwner,
  initialQuery: route.query,
  replaceQuery: query => router.replace({ query }),
  onSessionExpired: handleSessionExpired,
})

const showsItems = computed(() => resources.page.value !== null
  && (resources.state.value === 'success'
    || resources.state.value === 'stale'
    || resources.state.value === 'partial'))

async function copyInternalName(internalName: string) {
  try {
    if (navigator.clipboard === undefined)
      throw new Error('Clipboard API unavailable')
    await navigator.clipboard.writeText(internalName)
    toast.add({ title: t('gameResources.copy.success'), color: 'success' })
  }
  catch {
    toast.add({ title: t('gameResources.copy.failure'), color: 'error' })
  }
}
</script>

<template>
  <UDashboardPanel id="game-resources">
    <template #header>
      <UDashboardNavbar
        icon="i-lucide-package-search"
        :title="t('gameResources.title')"
      />
      <GameResourcesFilters
        :count="resources.page.value?.total ?? 0"
        :filters="resources.filters.value"
        :is-owner="isOwner"
        :is-refreshing="resources.isRefreshing.value"
        @include-hidden="resources.setIncludeHidden"
        @kind="resources.setKind"
        @refresh="resources.refresh"
        @search="resources.setSearch"
      />
    </template>

    <template #body>
      <GameResourcesStatus
        :state="resources.state.value"
        @clear="resources.clearFilters"
        @retry="resources.retry"
      />

      <template v-if="showsItems && resources.page.value">
        <GameResourcesMetadata :page="resources.page.value" />
        <p class="text-xs text-muted">
          {{ t('gameResources.tintHelp') }}
        </p>
        <GameResourcesTable
          :items="resources.page.value.items"
          @copy="copyInternalName"
        />
        <GameResourcesList
          :items="resources.page.value.items"
          @copy="copyInternalName"
        />
      </template>
    </template>

    <template v-if="resources.page.value && resources.page.value.total > 0" #footer>
      <div class="flex w-full justify-end px-4 py-3 sm:px-6">
        <UPagination
          :items-per-page="resources.page.value.pageSize"
          :page="resources.filters.value.page"
          :total="resources.page.value.total"
          @update:page="resources.setPage"
        />
      </div>
    </template>
  </UDashboardPanel>
</template>

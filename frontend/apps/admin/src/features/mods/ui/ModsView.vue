<script setup lang="ts">
import type { ModMetadata } from '../api/mods'

import { computed, shallowRef } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'

import { useMods } from '../model/useMods'
import ModStateDialog from './ModStateDialog.vue'

const { t } = useI18n()
const router = useRouter()
const controller = useMods({
  onSessionExpired: () => router.replace({ path: '/login', query: { redirect: '/mods' } }),
})
const search = shallowRef('')
const selected = shallowRef<ModMetadata | null>(null)
const targetEnabled = shallowRef(false)
const dialogOpen = computed({
  get: () => selected.value !== null,
  set: (open: boolean) => {
    if (!open && controller.changingDirectoryId.value === null)
      selected.value = null
  },
})
const visibleMods = computed(() => {
  const query = search.value.trim().toLocaleLowerCase()
  return [...controller.mods.value]
    .filter(mod => query === '' || [mod.displayName, mod.name, mod.author, mod.directoryId]
      .some(value => value.toLocaleLowerCase().includes(query)))
    .sort((left, right) => left.displayName.localeCompare(right.displayName))
})

function safeWebsite(website: string | null): string | null {
  if (website === null)
    return null
  try {
    const url = new URL(website)
    return url.protocol === 'http:' || url.protocol === 'https:' ? url.href : null
  }
  catch {
    return null
  }
}

function currentLabel(mod: ModMetadata) {
  if (mod.isLoadedNow === null)
    return t('mods.current.unknown')
  return t(mod.isLoadedNow ? 'mods.current.loaded' : 'mods.current.unloaded')
}

function openChange(mod: ModMetadata) {
  selected.value = mod
  targetEnabled.value = !mod.isEnabledNextStart
}

async function confirmChange() {
  if (selected.value === null)
    return
  if (await controller.changeNextStart(selected.value, targetEnabled.value))
    selected.value = null
}
</script>

<template>
  <UDashboardPanel id="mods">
    <template #header>
      <UDashboardNavbar :title="t('mods.title')">
        <template #leading>
          <UDashboardSidebarCollapse />
        </template>
        <template #right>
          <UButton icon="i-lucide-refresh-cw" color="neutral" variant="ghost" :label="t('common.reload')" @click="controller.refresh" />
        </template>
      </UDashboardNavbar>
    </template>

    <template #body>
      <div class="space-y-4">
        <UInput
          id="mods-search"
          v-model="search"
          :aria-label="t('mods.search')"
          icon="i-lucide-search"
          name="mods-search"
          :placeholder="t('mods.search')"
        />

        <p v-if="controller.state.value === 'loading'" class="text-sm text-muted">
          {{ t('mods.loading') }}
        </p>
        <p v-else-if="controller.state.value === 'empty'" class="text-sm text-muted">
          {{ t('mods.empty') }}
        </p>
        <p v-else-if="controller.state.value === 'failed'" class="text-sm text-error">
          {{ t('mods.failed') }}
        </p>

        <article
          v-for="mod in visibleMods"
          :key="mod.directoryId"
          class="grid gap-3 border-b border-default py-4 md:grid-cols-[minmax(0,1fr)_auto]"
        >
          <div class="min-w-0">
            <div class="flex flex-wrap items-center gap-2">
              <h2 class="font-medium text-highlighted">
                {{ mod.displayName }}
              </h2>
              <UBadge variant="subtle">
                {{ currentLabel(mod) }}
              </UBadge>
              <UBadge :color="mod.isEnabledNextStart ? 'success' : 'neutral'" variant="subtle">
                {{ t(mod.isEnabledNextStart ? 'mods.next.enabled' : 'mods.next.disabled') }}
              </UBadge>
            </div>
            <p class="mt-1 text-xs text-muted">
              {{ mod.author }} · {{ mod.version }} · {{ mod.directoryId }}
            </p>
            <p v-if="mod.description" class="mt-2 text-sm text-muted">
              {{ mod.description }}
            </p>
            <a
              v-if="safeWebsite(mod.website)"
              class="mt-2 inline-block text-sm text-primary"
              :href="safeWebsite(mod.website)!"
              target="_blank"
              rel="noopener noreferrer"
            >{{ t('mods.website') }}</a>
            <p class="mt-2 text-xs text-warning">
              {{ t('mods.restartHint') }}
            </p>
          </div>

          <div class="self-center">
            <span v-if="mod.isProtected" class="text-sm text-muted">{{ t('mods.protected') }}</span>
            <UButton
              v-else-if="controller.canMutate.value"
              :label="t(mod.isEnabledNextStart ? 'mods.action.disable' : 'mods.action.enable')"
              :loading="controller.changingDirectoryId.value === mod.directoryId"
              color="neutral"
              variant="outline"
              @click="openChange(mod)"
            />
          </div>
        </article>

        <p v-if="controller.feedback.value" role="status" class="text-sm text-warning">
          {{ t(`mods.feedback.${controller.feedback.value.code}`) }}
        </p>
      </div>
    </template>
  </UDashboardPanel>

  <ModStateDialog
    v-model:open="dialogOpen"
    :mod="selected"
    :enabled="targetEnabled"
    :submitting="controller.changingDirectoryId.value !== null"
    @confirm="confirmChange"
  />
</template>

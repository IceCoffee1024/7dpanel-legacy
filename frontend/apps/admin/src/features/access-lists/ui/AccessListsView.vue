<script setup lang="ts">
import type { BanEntry, BanInput, WhitelistEntry, WhitelistInput } from '../api/accessLists'

import { computed, onMounted, onUnmounted, shallowRef } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'
import { useAccessLists } from '../model/useAccessLists'
import BanDialog from './BanDialog.vue'
import WhitelistDialog from './WhitelistDialog.vue'

const route = useRoute()
const router = useRouter()
const { t } = useI18n()
const controller = useAccessLists()
const tab = computed<'ban' | 'whitelist'>(() => route.query.tab === 'whitelist' ? 'whitelist' : 'ban')
const query = computed(() => typeof route.query.q === 'string' ? route.query.q.trim().toLocaleLowerCase() : '')
const banDialogOpen = shallowRef(false)
const whitelistDialogOpen = shallowRef(false)
const selectedBan = shallowRef<BanEntry | null>(null)
const selectedWhitelist = shallowRef<WhitelistEntry | null>(null)
const visibleBans = computed(() => controller.bans.value.filter(item => `${item.playerId} ${item.displayName}`.toLocaleLowerCase().includes(query.value)))
const visibleWhitelist = computed(() => controller.whitelist.value.filter(item => `${item.playerId} ${item.displayName}`.toLocaleLowerCase().includes(query.value)))

function selectTab(next: 'ban' | 'whitelist') {
  void router.replace({ query: { ...route.query, tab: next } })
}
function addBan() { selectedBan.value = null; banDialogOpen.value = true }
function editBan(entry: BanEntry) { selectedBan.value = entry; banDialogOpen.value = true }
function addWhitelist() { selectedWhitelist.value = null; whitelistDialogOpen.value = true }
function editWhitelist(entry: WhitelistEntry) { selectedWhitelist.value = entry; whitelistDialogOpen.value = true }
async function saveBan(input: BanInput) { if (await controller.saveBan(input)) banDialogOpen.value = false }
async function saveWhitelist(input: WhitelistInput) { if (await controller.saveWhitelist(input)) whitelistDialogOpen.value = false }
function removeBan(entry: BanEntry) {
  if (window.confirm(t('accessLists.confirm.removeBan', { name: entry.displayName || entry.playerId })))
    void controller.removeBan(entry.playerId)
}
function removeWhitelist(entry: WhitelistEntry) {
  if (window.confirm(t('accessLists.confirm.removeWhitelist', { name: entry.displayName || entry.playerId })))
    void controller.removeWhitelist(entry.playerId)
}
onMounted(() => { void controller.refreshBans(); void controller.refreshWhitelist() })
onUnmounted(controller.dispose)
</script>

<template>
  <UDashboardPanel id="access-lists">
    <template #header>
      <div class="flex flex-wrap items-center justify-between gap-3 p-4">
        <div><h1 class="text-lg font-semibold">{{ t('accessLists.title') }}</h1><p class="text-sm text-muted">{{ t('accessLists.description') }}</p></div>
        <div class="flex gap-2"><UButton :label="t('accessLists.tabs.bans')" :variant="tab === 'ban' ? 'solid' : 'outline'" @click="selectTab('ban')" /><UButton :label="t('accessLists.tabs.whitelist')" :variant="tab === 'whitelist' ? 'solid' : 'outline'" @click="selectTab('whitelist')" /></div>
      </div>
    </template>
    <template #body>
      <div class="space-y-4 p-4">
        <UAlert v-if="(tab === 'ban' ? controller.banState.value : controller.whitelistState.value) === 'game-not-ready'" color="warning" :title="t('accessLists.state.gameNotReady')" />
        <UAlert v-else-if="(tab === 'ban' ? controller.banState.value : controller.whitelistState.value) === 'failed'" color="error" :title="t('accessLists.state.failed')" />
        <UAlert v-else-if="!controller.canMutate.value" color="neutral" :title="t('accessLists.state.readOnlyTitle')" :description="t('accessLists.state.readOnlyDescription')" />
        <div class="flex justify-end">
          <UButton v-if="tab === 'ban' && controller.canMutate.value" data-testid="add-ban" :label="t('accessLists.action.addBan')" @click="addBan" />
          <UButton v-if="tab === 'whitelist' && controller.canMutate.value" data-testid="add-whitelist" :label="t('accessLists.action.addWhitelist')" @click="addWhitelist" />
        </div>
        <div v-if="tab === 'ban'" class="space-y-2">
          <article v-for="entry in visibleBans" :key="entry.playerId" class="flex items-center justify-between gap-3 rounded-lg border border-default p-3">
            <div><p class="font-medium">{{ entry.displayName || entry.playerId }}</p><p class="text-xs text-muted">{{ entry.playerId }} · {{ entry.bannedUntilUtc ?? t('accessLists.permanent') }}</p><p v-if="entry.reason" class="text-sm">{{ entry.reason }}</p></div>
            <div v-if="controller.canMutate.value" class="flex gap-2"><UButton :data-testid="`edit-ban-${entry.playerId}`" :label="t('common.edit')" variant="outline" @click="editBan(entry)" /><UButton :label="t('accessLists.action.remove')" color="error" variant="outline" @click="removeBan(entry)" /></div>
          </article>
          <p v-if="visibleBans.length === 0" class="text-sm text-muted">{{ t('accessLists.empty.bans') }}</p>
        </div>
        <div v-else class="space-y-2">
          <article v-for="entry in visibleWhitelist" :key="entry.playerId" class="flex items-center justify-between gap-3 rounded-lg border border-default p-3">
            <div><p class="font-medium">{{ entry.displayName || entry.playerId }}</p><p class="text-xs text-muted">{{ entry.playerId }}</p></div>
            <div v-if="controller.canMutate.value" class="flex gap-2"><UButton :data-testid="`edit-whitelist-${entry.playerId}`" :label="t('common.edit')" variant="outline" @click="editWhitelist(entry)" /><UButton :label="t('accessLists.action.remove')" color="error" variant="outline" @click="removeWhitelist(entry)" /></div>
          </article>
          <p v-if="visibleWhitelist.length === 0" class="text-sm text-muted">{{ t('accessLists.empty.whitelist') }}</p>
        </div>
      </div>
    </template>
  </UDashboardPanel>
  <BanDialog v-model:open="banDialogOpen" :entry="selectedBan" @save="saveBan" />
  <WhitelistDialog v-model:open="whitelistDialogOpen" :entry="selectedWhitelist" @save="saveWhitelist" />
</template>

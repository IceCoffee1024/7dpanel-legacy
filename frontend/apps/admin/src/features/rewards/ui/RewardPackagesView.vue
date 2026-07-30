<script setup lang="ts">
import type { RewardPackageDraft, RewardPackageEntryDraft } from '../api/rewards'
import type { RewardPackagesController } from '../model/useRewards'

import { computed, reactive, shallowRef, watch } from 'vue'
import { useI18n } from 'vue-i18n'

const props = defineProps<{ controller: RewardPackagesController }>()
const emit = defineEmits<{ load: [packageId: string], save: [draft: RewardPackageDraft] }>()
const { t } = useI18n()
const lookupId = shallowRef('')
const draft = reactive({ packageId: '', name: '', description: '', enabled: true, sortOrder: 0, entries: [] as RewardPackageEntryDraft[] })
const valid = computed(() => draft.packageId.trim() !== '' && draft.name.trim() !== '' && draft.entries.length > 0)

watch(() => props.controller.rewardPackage.value, (value) => {
  if (value === null)
    return
  draft.packageId = value.packageId
  draft.name = value.name
  draft.description = value.description
  draft.enabled = value.enabled
  draft.sortOrder = value.sortOrder
  draft.entries = value.entries.map(entry => ({
    entryId: entry.entryId,
    kind: entry.kind,
    ...(entry.itemInternalName === null ? {} : { itemInternalName: entry.itemInternalName }),
    ...(entry.itemKind === null ? {} : { itemKind: entry.itemKind }),
    ...(entry.quantity === null ? {} : { quantity: entry.quantity }),
    ...(entry.minQuality === null ? {} : { minQuality: entry.minQuality }),
    ...(entry.maxQuality === null ? {} : { maxQuality: entry.maxQuality }),
    ...(entry.catalogVersion === null ? {} : { catalogVersion: entry.catalogVersion }),
    ...(entry.currencyAmount === null ? {} : { currencyAmount: entry.currencyAmount }),
    ...(entry.registeredAction === null ? {} : { registeredAction: entry.registeredAction }),
  }))
}, { immediate: true })

function createPackage() {
  const id = lookupId.value.trim()
  draft.packageId = id
  draft.name = ''
  draft.description = ''
  draft.enabled = true
  draft.sortOrder = 0
  draft.entries = []
}
function addItem() {
  draft.entries.push({ entryId: crypto.randomUUID(), kind: 'Item', itemKind: 'Item', itemInternalName: '', quantity: 1, catalogVersion: '' })
}
function addCurrency() {
  draft.entries.push({ entryId: crypto.randomUUID(), kind: 'Currency', currencyAmount: 1n })
}
function updateCurrency(index: number, value: string) {
  const entry = draft.entries[index]
  if (entry && /^\d+$/.test(value))
    draft.entries[index] = { ...entry, currencyAmount: BigInt(value) }
}
function save() {
  if (valid.value)
    emit('save', { ...draft, entries: draft.entries.map(entry => ({ ...entry })) })
}
</script>

<template>
  <UDashboardPanel id="reward-packages">
    <template #header>
      <UDashboardNavbar :title="t('rewards.packages.title')">
        <template #leading>
          <UDashboardSidebarCollapse />
        </template>
      </UDashboardNavbar>
    </template>
    <template #body>
      <UContainer class="space-y-5 py-5">
        <UAlert
          v-if="props.controller.state.value === 'stale'"
          color="warning"
          :title="t('rewards.packages.state.stale')"
          :description="props.controller.errorCode.value ?? undefined"
        />
        <UAlert
          v-else-if="props.controller.state.value === 'failed' || props.controller.state.value === 'forbidden'"
          color="error"
          :title="t(props.controller.state.value === 'forbidden' ? 'rewards.packages.state.forbidden' : 'rewards.packages.state.unavailable')"
          :description="props.controller.errorCode.value ?? undefined"
        />
        <UCard>
          <div class="grid gap-3 md:grid-cols-[minmax(0,1fr)_auto_auto] md:items-end">
            <UFormField :label="t('rewards.packages.packageId')">
              <UInput v-model="lookupId" class="w-full" />
            </UFormField><UButton
              color="neutral"
              :label="t('rewards.common.load')"
              variant="outline"
              @click="emit('load', lookupId.trim())"
            /><UButton :label="t('rewards.packages.createWithId')" variant="soft" @click="createPackage" />
          </div>
        </UCard>
        <UCard v-if="draft.packageId">
          <template #header>
            <div>
              <h2 class="font-semibold">
                {{ draft.packageId }}
              </h2><p class="text-sm text-muted">
                {{ t('rewards.packages.saveNotice') }}
              </p>
            </div>
          </template>
          <div class="grid gap-3 md:grid-cols-2">
            <UFormField :label="t('rewards.packages.name')">
              <UInput v-model="draft.name" class="w-full" />
            </UFormField>
            <UFormField :label="t('rewards.packages.sortOrder')">
              <UInput v-model.number="draft.sortOrder" class="w-full" type="number" />
            </UFormField>
            <UFormField class="md:col-span-2" :label="t('rewards.packages.description')">
              <UTextarea v-model="draft.description" class="w-full" />
            </UFormField>
            <UCheckbox v-model="draft.enabled" :label="t('rewards.packages.enabled')" />
          </div>
          <div class="mt-5 space-y-3">
            <div class="flex flex-wrap items-center justify-between gap-2">
              <h3 class="font-medium">
                {{ t('rewards.packages.entries.title') }}
              </h3><div class="flex gap-2">
                <UButton
                  color="neutral"
                  :label="t('rewards.packages.entries.addItem')"
                  size="sm"
                  variant="outline"
                  @click="addItem"
                /><UButton
                  color="neutral"
                  :label="t('rewards.packages.entries.addCurrency')"
                  size="sm"
                  variant="outline"
                  @click="addCurrency"
                />
              </div>
            </div>
            <div v-for="(entry, index) in draft.entries" :key="entry.entryId" class="rounded-lg border border-default p-3">
              <div class="mb-3 flex items-center justify-between">
                <UBadge color="neutral" variant="subtle">
                  {{ entry.kind }}
                </UBadge><UButton
                  color="error"
                  icon="i-lucide-trash-2"
                  square
                  variant="ghost"
                  @click="draft.entries.splice(index, 1)"
                />
              </div>
              <div v-if="entry.kind === 'Item'" class="grid gap-3 md:grid-cols-3">
                <UFormField :label="t('rewards.packages.entries.internalName')">
                  <UInput v-model="entry.itemInternalName" class="w-full" />
                </UFormField>
                <UFormField :label="t('rewards.packages.entries.catalogVersion')">
                  <UInput v-model="entry.catalogVersion" class="w-full" />
                </UFormField>
                <UFormField :label="t('rewards.packages.entries.quantity')">
                  <UInput
                    v-model.number="entry.quantity"
                    class="w-full"
                    min="1"
                    type="number"
                  />
                </UFormField>
              </div>
              <UFormField v-else-if="entry.kind === 'Currency'" :label="t('rewards.packages.entries.currencyAmount')">
                <UInput
                  :model-value="entry.currencyAmount?.toString() ?? ''"
                  class="w-full"
                  inputmode="numeric"
                  @update:model-value="updateCurrency(index, String($event))"
                />
              </UFormField>
            </div>
          </div>
          <template #footer>
            <div class="flex justify-end">
              <UButton
                :label="t('rewards.packages.save')"
                :disabled="!valid"
                :loading="props.controller.isMutating.value"
                @click="save"
              />
            </div>
          </template>
        </UCard>
      </UContainer>
    </template>
  </UDashboardPanel>
</template>

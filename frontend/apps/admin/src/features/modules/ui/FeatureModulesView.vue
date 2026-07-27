<script setup lang="ts">
import type { TableColumn } from '@nuxt/ui'
import type { FeatureModule, FeatureModuleLifecycleState } from '../api/modules'

import { computed, shallowRef } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'

import { useAuthStore } from '../../auth'
import { useFeatureModules } from '../model/useFeatureModules'

const auth = useAuthStore()
const route = useRoute()
const router = useRouter()
const { t } = useI18n()
const enableTarget = shallowRef<FeatureModule | null>(null)
const disableTarget = shallowRef<FeatureModule | null>(null)

function handleSessionExpired() {
  auth.expireSession()
  void router.replace({ path: '/login', query: { redirect: route.fullPath } })
}

const featureModules = useFeatureModules({ onSessionExpired: handleSessionExpired })
const columns = computed<TableColumn<FeatureModule>[]>(() => [
  { accessorKey: 'moduleId', header: t('modules.table.module') },
  { accessorKey: 'lifecycleState', header: t('modules.table.lifecycle') },
  { id: 'health', header: t('modules.table.health') },
  { id: 'details', header: t('modules.table.details') },
  { id: 'actions', header: '' },
])
const isMutating = computed(() => featureModules.pendingModuleId.value !== null)
const tableData = computed<FeatureModule[]>(() => [...featureModules.modules.value])

function lifecycleColor(state: FeatureModuleLifecycleState): 'success' | 'neutral' | 'warning' {
  if (state === 'Enabled') return 'success'
  if (state === 'Draining' || state === 'RestartRequired') return 'warning'
  return 'neutral'
}

function list(values: readonly string[]): string {
  return values.length === 0 ? t('modules.common.none') : values.join(', ')
}

function observed(value: string): string {
  return new Date(value).toLocaleString()
}

function openEnable(module: FeatureModule) {
  if (featureModules.canMutate.value && module.isToggleable && !module.isEnabled)
    enableTarget.value = module
}

function openDisable(module: FeatureModule) {
  if (featureModules.canMutate.value && module.isToggleable && module.isEnabled)
    disableTarget.value = module
}

function updateEnableOpen(open: boolean) {
  if (!open && !isMutating.value)
    enableTarget.value = null
}

function updateDisableOpen(open: boolean) {
  if (!open && !isMutating.value)
    disableTarget.value = null
}

async function confirmEnable() {
  const target = enableTarget.value
  if (target !== null && await featureModules.enable(target.moduleId, target.rowVersion))
    enableTarget.value = null
}

async function confirmDisable() {
  const target = disableTarget.value
  if (target !== null && await featureModules.disable(target.moduleId, target.rowVersion))
    disableTarget.value = null
}
</script>

<template>
  <UDashboardPanel id="feature-modules">
    <template #header>
      <UDashboardNavbar icon="i-lucide-blocks" :title="t('modules.title')">
        <template #leading><UDashboardSidebarCollapse /></template>
        <template #right>
          <UButton
            color="neutral"
            icon="i-lucide-refresh-cw"
            :label="t('modules.common.refresh')"
            :loading="featureModules.state.value === 'loading'"
            variant="outline"
            @click="featureModules.refresh"
          />
        </template>
      </UDashboardNavbar>
    </template>

    <template #body>
      <div class="mx-auto w-full max-w-7xl space-y-4 sm:space-y-6">
        <UAlert
          color="neutral"
          icon="i-lucide-activity"
          :title="t('modules.healthUnknownTitle')"
          :description="t('modules.healthUnknownDescription')"
          variant="subtle"
        />
        <UAlert
          v-if="featureModules.errorCode.value"
          color="error"
          icon="i-lucide-circle-alert"
          :title="t('modules.stateUnavailable')"
          :description="t('modules.common.errorCode', { code: featureModules.errorCode.value })"
          variant="subtle"
        />

        <div v-if="featureModules.state.value === 'loading' && featureModules.modules.value.length === 0" class="space-y-3">
          <USkeleton v-for="row in 6" :key="row" class="h-20 w-full" />
        </div>
        <template v-else-if="featureModules.modules.value.length > 0">
          <div class="hidden lg:block">
            <UTable :columns="columns" :data="tableData">
              <template #moduleId-cell="{ row }">
                <div class="max-w-56 space-y-1">
                  <p class="break-words font-medium text-highlighted">{{ row.original.moduleId }}</p>
                  <UBadge :color="row.original.isEnabled ? 'success' : 'neutral'" variant="subtle">
                    {{ row.original.isEnabled ? t('modules.status.enabled') : t('modules.status.disabled') }}
                  </UBadge>
                </div>
              </template>
              <template #lifecycleState-cell="{ row }">
                <div class="space-y-1">
                  <UBadge :color="lifecycleColor(row.original.lifecycleState)" variant="subtle">{{ row.original.lifecycleState }}</UBadge>
                  <p class="text-xs text-muted">{{ t('modules.fields.disableMode') }}: {{ row.original.disableMode }}</p>
                </div>
              </template>
              <template #health-cell="{ row }">
                <div class="max-w-44 space-y-1">
                  <UBadge color="neutral" variant="subtle">{{ t('modules.status.unknown') }}</UBadge>
                  <p class="break-words text-xs text-muted">{{ t('modules.fields.healthSource') }}: {{ row.original.healthSource }}</p>
                </div>
              </template>
              <template #details-cell="{ row }">
                <dl class="max-w-xl space-y-1 text-xs">
                  <div><dt class="inline text-muted">{{ t('modules.fields.dependencies') }}: </dt><dd class="inline break-words">{{ list(row.original.dependencies) }}</dd></div>
                  <div><dt class="inline text-muted">{{ t('modules.fields.settingsSummary') }}: </dt><dd class="inline break-words">{{ list(row.original.settingsSummaryFields) }}</dd></div>
                  <div><dt class="inline text-muted">{{ t('modules.fields.dataRetention') }}: </dt><dd class="inline break-words">{{ row.original.dataRetentionSummary }}</dd></div>
                  <div><dt class="inline text-muted">{{ t('modules.fields.consumers') }}: </dt><dd class="inline break-words">{{ list(row.original.consumerIds) }}</dd></div>
                  <div><dt class="inline text-muted">{{ t('modules.fields.updated') }}: </dt><dd class="inline">{{ t('modules.fields.updatedBy', { time: observed(row.original.updatedAtUtc), operator: row.original.updatedBy }) }}</dd></div>
                  <div><dt class="inline text-muted">{{ t('modules.fields.rowVersion') }}: </dt><dd class="inline">{{ row.original.rowVersion }}</dd></div>
                </dl>
              </template>
              <template #actions-cell="{ row }">
                <UButton
                  v-if="featureModules.canMutate.value && row.original.isToggleable && row.original.isEnabled"
                  color="error"
                  :label="t('modules.actions.disable')"
                  variant="soft"
                  :disabled="isMutating"
                  @click="openDisable(row.original)"
                />
                <UButton
                  v-else-if="featureModules.canMutate.value && row.original.isToggleable"
                  color="primary"
                  :label="t('modules.actions.enable')"
                  variant="soft"
                  :disabled="isMutating"
                  @click="openEnable(row.original)"
                />
              </template>
            </UTable>
          </div>

          <div class="grid gap-4 lg:hidden">
            <article v-for="module in featureModules.modules.value" :key="module.moduleId" class="min-w-0 space-y-4 rounded-lg border border-default p-4">
              <div class="flex flex-wrap items-start justify-between gap-2">
                <h2 class="break-words font-semibold text-highlighted">{{ module.moduleId }}</h2>
                <div class="flex flex-wrap gap-2">
                  <UBadge :color="module.isEnabled ? 'success' : 'neutral'" variant="subtle">{{ module.isEnabled ? t('modules.status.enabled') : t('modules.status.disabled') }}</UBadge>
                  <UBadge :color="lifecycleColor(module.lifecycleState)" variant="subtle">{{ module.lifecycleState }}</UBadge>
                </div>
              </div>
              <dl class="grid min-w-0 gap-3 text-sm sm:grid-cols-2">
                <div><dt class="text-xs text-muted">{{ t('modules.fields.dependencies') }}</dt><dd class="break-words">{{ list(module.dependencies) }}</dd></div>
                <div><dt class="text-xs text-muted">{{ t('modules.table.health') }}</dt><dd><UBadge color="neutral" variant="subtle">{{ t('modules.status.unknown') }}</UBadge><span class="ml-2 break-words text-xs text-muted">{{ module.healthSource }}</span></dd></div>
                <div><dt class="text-xs text-muted">{{ t('modules.fields.settingsSummary') }}</dt><dd class="break-words">{{ list(module.settingsSummaryFields) }}</dd></div>
                <div><dt class="text-xs text-muted">{{ t('modules.fields.dataRetention') }}</dt><dd class="break-words">{{ module.dataRetentionSummary }}</dd></div>
                <div><dt class="text-xs text-muted">{{ t('modules.fields.disableMode') }}</dt><dd>{{ module.disableMode }}</dd></div>
                <div><dt class="text-xs text-muted">{{ t('modules.fields.consumers') }}</dt><dd class="break-words">{{ list(module.consumerIds) }}</dd></div>
                <div><dt class="text-xs text-muted">{{ t('modules.fields.updated') }}</dt><dd>{{ t('modules.fields.updatedBy', { time: observed(module.updatedAtUtc), operator: module.updatedBy }) }}</dd></div>
                <div><dt class="text-xs text-muted">{{ t('modules.fields.rowVersion') }}</dt><dd>{{ module.rowVersion }}</dd></div>
              </dl>
              <div v-if="featureModules.canMutate.value && module.isToggleable" class="flex justify-end">
                <UButton v-if="module.isEnabled" color="error" :label="t('modules.actions.disable')" variant="soft" :disabled="isMutating" @click="openDisable(module)" />
                <UButton v-else color="primary" :label="t('modules.actions.enable')" variant="soft" :disabled="isMutating" @click="openEnable(module)" />
              </div>
            </article>
          </div>
        </template>
        <UAlert
          v-else
          color="error"
          icon="i-lucide-blocks"
          :title="t('modules.unavailableTitle')"
          :description="t('modules.unavailableDescription')"
          variant="subtle"
        />
      </div>

      <UModal
        :open="enableTarget !== null"
        :dismissible="!isMutating"
        :title="t('modules.enableDialog.title')"
        :description="t('modules.enableDialog.description')"
        :ui="{ footer: 'justify-end' }"
        @update:open="updateEnableOpen"
      >
        <template #body>
          <div v-if="enableTarget" class="space-y-4">
            <UAlert color="warning" :title="t('modules.enableDialog.dependencyRevalidation')" variant="subtle" />
            <UFormField :label="t('modules.enableDialog.module')"><p class="break-all text-sm text-highlighted">{{ enableTarget.moduleId }}</p></UFormField>
            <UFormField :label="t('modules.enableDialog.expectedRowVersion')"><p class="text-sm text-highlighted">{{ enableTarget.rowVersion }}</p></UFormField>
            <UFormField :label="t('modules.enableDialog.dependencies')"><p class="break-words text-sm text-highlighted">{{ list(enableTarget.dependencies) }}</p></UFormField>
          </div>
        </template>
        <template #footer>
          <UButton color="neutral" :label="t('modules.common.cancel')" variant="outline" :disabled="isMutating" @click="enableTarget = null" />
          <UButton color="primary" :label="t('modules.enableDialog.confirm')" :loading="featureModules.pendingMutation.value === 'enable'" @click="confirmEnable" />
        </template>
      </UModal>

      <UModal
        :open="disableTarget !== null"
        :dismissible="!isMutating"
        :title="t('modules.disableDialog.title')"
        :description="t('modules.disableDialog.description')"
        :ui="{ footer: 'justify-end' }"
        @update:open="updateDisableOpen"
      >
        <template #body>
          <div v-if="disableTarget" class="space-y-4">
            <UAlert color="warning" icon="i-lucide-triangle-alert" :title="t('modules.disableDialog.dangerTitle')" :description="t('modules.disableDialog.dangerDescription', { disableMode: disableTarget.disableMode })" variant="subtle" />
            <UFormField :label="t('modules.disableDialog.module')"><p class="break-all text-sm text-highlighted">{{ disableTarget.moduleId }}</p></UFormField>
            <UFormField :label="t('modules.disableDialog.expectedRowVersion')"><p class="text-sm text-highlighted">{{ disableTarget.rowVersion }}</p></UFormField>
            <UFormField :label="t('modules.disableDialog.dataRetention')"><p class="break-words text-sm text-highlighted">{{ disableTarget.dataRetentionSummary }}</p></UFormField>
          </div>
        </template>
        <template #footer>
          <UButton color="neutral" :label="t('modules.common.cancel')" variant="outline" :disabled="isMutating" @click="disableTarget = null" />
          <UButton color="error" :label="t('modules.disableDialog.confirm')" :loading="featureModules.pendingMutation.value === 'disable'" @click="confirmDisable" />
        </template>
      </UModal>
    </template>
  </UDashboardPanel>
</template>

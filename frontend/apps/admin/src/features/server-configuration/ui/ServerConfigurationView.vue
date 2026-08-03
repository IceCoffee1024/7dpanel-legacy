<script setup lang="ts">
import type { ServerConfigurationField } from '../api/serverConfiguration'

import { computed, shallowRef } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'

import { useServerConfiguration } from '../model/useServerConfiguration'
import ServerConfigurationFieldEditor from './ServerConfigurationFieldEditor.vue'

const router = useRouter()
const { t } = useI18n()
const controller = useServerConfiguration({
  onSessionExpired: () => router.replace({ path: '/login', query: { redirect: '/operations/configuration' } }),
})
const selected = shallowRef<ServerConfigurationField | null>(null)
const inputValue = shallowRef('')
const groupedFields = computed(() => {
  const groups = new Map<string, ServerConfigurationField[]>()
  for (const field of controller.snapshot.value?.fields ?? []) {
    const group = groups.get(field.group) ?? []
    group.push(field)
    groups.set(field.group, group)
  }
  return [...groups.entries()]
})

function edit(field: ServerConfigurationField) {
  controller.clearFeedback()
  selected.value = field
  inputValue.value = field.value
}

async function save() {
  if (selected.value === null)
    return
  if (await controller.update(selected.value.key, inputValue.value))
    selected.value = null
}
</script>

<template>
  <UDashboardPanel id="server-configuration">
    <template #header>
      <UDashboardNavbar :title="t('serverConfiguration.title')">
        <template #leading>
          <UDashboardSidebarCollapse />
        </template>
        <template #right>
          <UButton
            color="neutral"
            icon="i-lucide-server"
            :label="t('serverOperations.title')"
            href="/operations/server"
            variant="ghost"
          />
          <UButton
            icon="i-lucide-refresh-cw"
            color="neutral"
            variant="ghost"
            :loading="controller.isRefreshing.value"
            :label="t('common.reload')"
            @click="controller.refresh"
          />
        </template>
      </UDashboardNavbar>
    </template>
    <template #body>
      <div v-if="controller.state.value === 'loading'" class="space-y-3" data-testid="configuration-loading">
        <USkeleton v-for="row in 5" :key="row" class="h-20 w-full" />
      </div>
      <UAlert v-else-if="controller.state.value === 'forbidden'" color="warning" :title="t('serverConfiguration.state.forbidden')" />
      <UAlert v-else-if="controller.state.value === 'failed'" color="error" :title="t('serverConfiguration.state.failed')" />
      <div v-else class="space-y-5">
        <UAlert v-if="controller.state.value === 'stale'" color="warning" :title="t('serverConfiguration.state.stale')" />
        <section v-for="[group, fields] in groupedFields" :key="group" class="space-y-2">
          <h2 class="font-semibold text-highlighted">
            {{ t(`serverConfiguration.groups.${group.toLowerCase()}`, group) }}
          </h2>
          <article v-for="field in fields" :key="field.key" class="grid gap-3 rounded-lg border border-default p-4 sm:grid-cols-[minmax(0,1fr)_auto] sm:items-center">
            <div class="min-w-0">
              <div class="flex flex-wrap items-center gap-2">
                <strong>{{ t(`serverConfiguration.fields.${field.key}`, field.key) }}</strong>
                <UBadge v-if="field.advanced" color="info" variant="subtle">
                  {{ t('serverConfiguration.advanced') }}
                </UBadge>
                <UBadge v-if="field.restartRequired" color="warning" variant="subtle">
                  {{ t('serverConfiguration.restartRequired') }}
                </UBadge>
                <UBadge v-if="!field.editable" color="neutral" variant="subtle">
                  {{ t('serverConfiguration.readOnly') }}
                </UBadge>
              </div>
              <p class="mt-1 break-words text-sm text-muted" :data-testid="`configuration-value-${field.key}`">
                {{ field.sensitive ? t(field.isSet ? 'serverConfiguration.secretSet' : 'serverConfiguration.secretUnset') : field.value }}
              </p>
            </div>
            <UButton
              v-if="field.editable"
              :data-testid="`edit-${field.key}`"
              color="neutral"
              variant="outline"
              icon="i-lucide-pencil"
              :label="t('common.edit')"
              @click="edit(field)"
            />
          </article>
        </section>
      </div>
    </template>
  </UDashboardPanel>

  <UModal :open="selected !== null" :title="selected ? t(`serverConfiguration.fields.${selected.key}`, selected.key) : ''" @update:open="open => { if (!open && controller.updatingKey.value === null) selected = null }">
    <template #body>
      <div v-if="selected" class="space-y-4">
        <UAlert v-if="selected.advanced" color="warning" :title="t('serverConfiguration.advancedWarning')" />
        <ServerConfigurationFieldEditor v-model="inputValue" :field="selected" />
        <p class="text-sm text-muted">
          {{ selected.restartRequired ? t('serverConfiguration.confirmRestart') : t('serverConfiguration.confirmImmediate') }}
        </p>
        <UAlert v-if="controller.feedback.value" color="error" :title="t(`serverConfiguration.feedback.${controller.feedback.value.code}`)" />
        <div class="flex justify-end gap-2">
          <UButton
            color="neutral"
            variant="outline"
            :label="t('common.cancel')"
            @click="selected = null"
          />
          <UButton :loading="controller.updatingKey.value !== null" :label="t('common.save')" @click="save" />
        </div>
      </div>
    </template>
  </UModal>
</template>

<script setup lang="ts">
import type { ScheduleDraft, ScheduleRecord, SchedulesController } from '../model/useSchedules'

import { shallowRef } from 'vue'
import { useI18n } from 'vue-i18n'

import AnnouncementForm from './AnnouncementForm.vue'
import ScheduleForm from './ScheduleForm.vue'
import ScheduleTable from './ScheduleTable.vue'

const props = defineProps<{ controller: SchedulesController }>()
const { t } = useI18n()
const formOpen = shallowRef(false)
const editing = shallowRef<ScheduleRecord | null>(null)
const deleteTarget = shallowRef<ScheduleRecord | null>(null)

function openCreate() {
  editing.value = null
  formOpen.value = true
}

function openEdit(schedule: ScheduleRecord) {
  editing.value = schedule
  formOpen.value = true
}

async function save(draft: ScheduleDraft) {
  if (await props.controller.save(draft))
    formOpen.value = false
}

function remove(schedule: ScheduleRecord) {
  deleteTarget.value = schedule
}

function confirmDelete() {
  const schedule = deleteTarget.value
  if (schedule === null)
    return
  deleteTarget.value = null
  void props.controller.remove(schedule)
}
</script>

<template>
  <UDashboardPanel id="schedules">
    <template #header>
      <UDashboardNavbar :title="t('schedules.title')">
        <template #leading>
          <UDashboardSidebarCollapse />
        </template>
        <template #right>
          <UButton
            color="neutral"
            :disabled="controller.isMutating.value"
            icon="i-lucide-refresh-cw"
            :label="t('common.reload')"
            variant="outline"
            @click="controller.refresh"
          />
        </template>
      </UDashboardNavbar>
    </template>

    <template #body>
      <UContainer class="space-y-5 py-5">
        <UAlert v-if="controller.state.value === 'forbidden'" color="error" :title="t('schedules.state.forbidden')" />
        <UAlert v-else-if="controller.state.value === 'failed'" color="error" :title="t('schedules.state.failed')" />
        <UAlert v-else-if="controller.state.value === 'protocol-error'" color="error" :title="t('schedules.state.protocolError')" />
        <UAlert v-else-if="controller.state.value === 'stale'" color="warning" :title="t('schedules.state.stale')" />
        <UAlert
          v-if="controller.errorCode.value"
          color="error"
          :description="controller.errorCode.value"
          :title="t('schedules.state.operationFailed')"
        />

        <AnnouncementForm :disabled="controller.isMutating.value" @submit="controller.announce" />

        <UCard>
          <template #header>
            <div class="flex flex-wrap items-center justify-between gap-3">
              <div>
                <h2 class="font-semibold">
                  {{ t('schedules.catalog.title') }}
                </h2>
                <p class="text-sm text-muted">
                  {{ t('schedules.catalog.description') }}
                </p>
              </div>
              <UButton
                :disabled="controller.isMutating.value"
                icon="i-lucide-plus"
                :label="t('schedules.form.create')"
                @click="openCreate"
              />
            </div>
          </template>
          <ScheduleTable
            :disabled="controller.isMutating.value"
            :schedules="controller.schedules.value"
            @edit="openEdit"
            @remove="remove"
            @set-enabled="controller.setEnabled"
          />
        </UCard>
      </UContainer>
    </template>
  </UDashboardPanel>

  <UModal v-model:open="formOpen" :description="t('schedules.form.description')" :title="editing === null ? t('schedules.form.create') : t('schedules.form.edit')">
    <template #body>
      <ScheduleForm
        :disabled="controller.isMutating.value"
        :schedule="editing"
        @cancel="formOpen = false"
        @submit="save"
      />
    </template>
  </UModal>
  <UModal
    :open="deleteTarget !== null"
    :title="t('common.confirm')"
    :description="deleteTarget === null ? '' : t('schedules.delete.confirm', { name: deleteTarget.name })"
    :ui="{ footer: 'justify-end' }"
    @update:open="open => { if (!open) deleteTarget = null }"
  >
    <template #footer>
      <UButton
        color="neutral"
        :label="t('common.cancel')"
        variant="outline"
        @click="deleteTarget = null"
      />
      <UButton color="error" :label="t('common.confirm')" @click="confirmDelete" />
    </template>
  </UModal>
</template>

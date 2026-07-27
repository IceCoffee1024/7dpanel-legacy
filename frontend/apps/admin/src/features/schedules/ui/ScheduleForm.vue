<script setup lang="ts">
import type { ScheduleConcurrencyPolicy, ScheduleDraft, ScheduleKind, ScheduleRecord } from '../model/useSchedules'

import { reactive, watch } from 'vue'
import { useI18n } from 'vue-i18n'

const props = defineProps<{ disabled: boolean, schedule: ScheduleRecord | null }>()
const emit = defineEmits<{ cancel: [], submit: [draft: ScheduleDraft] }>()
const { t } = useI18n()

const form = reactive({
  name: '',
  cronExpression: '',
  timeZoneId: 'UTC',
  enabled: true,
  concurrencyPolicy: 'SkipIfRunning' as ScheduleConcurrencyPolicy,
  kind: 'ScheduledAnnouncement' as ScheduleKind,
  commandText: '',
  countdownSeconds: 60,
  messageText: '',
})

const kindItems = [
  { label: t('schedules.kind.ScheduledConsoleCommand'), value: 'ScheduledConsoleCommand' },
  { label: t('schedules.kind.ScheduledRestart'), value: 'ScheduledRestart' },
  { label: t('schedules.kind.ScheduledAnnouncement'), value: 'ScheduledAnnouncement' },
]
const policyItems = [
  { label: t('schedules.policy.SkipIfRunning'), value: 'SkipIfRunning' },
  { label: t('schedules.policy.QueueOne'), value: 'QueueOne' },
]

watch(() => props.schedule, (schedule) => {
  Object.assign(form, schedule === null
    ? {
        name: '',
        cronExpression: '',
        timeZoneId: 'UTC',
        enabled: true,
        concurrencyPolicy: 'SkipIfRunning',
        kind: 'ScheduledAnnouncement',
        commandText: '',
        countdownSeconds: 60,
        messageText: '',
      }
    : {
        name: schedule.name,
        cronExpression: schedule.cronExpression,
        timeZoneId: schedule.timeZoneId,
        enabled: schedule.enabled,
        concurrencyPolicy: schedule.concurrencyPolicy,
        kind: schedule.kind,
        commandText: schedule.commandText ?? '',
        countdownSeconds: schedule.countdownSeconds ?? 60,
        messageText: schedule.messageText ?? '',
      })
}, { immediate: true })

function submit() {
  emit('submit', {
    ...(props.schedule === null ? {} : { id: props.schedule.id, rowVersion: props.schedule.rowVersion }),
    name: form.name,
    cronExpression: form.cronExpression,
    timeZoneId: form.timeZoneId,
    enabled: form.enabled,
    concurrencyPolicy: form.concurrencyPolicy,
    kind: form.kind,
    commandText: form.kind === 'ScheduledConsoleCommand' ? form.commandText : null,
    countdownSeconds: form.kind === 'ScheduledRestart' ? form.countdownSeconds : null,
    messageText: form.kind === 'ScheduledAnnouncement' ? form.messageText : null,
  })
}
</script>

<template>
  <UForm class="space-y-4" :state="form" @submit="submit">
    <div class="grid gap-4 md:grid-cols-2">
      <UFormField :label="t('schedules.form.name')" name="name" required>
        <UInput v-model="form.name" :disabled="disabled" />
      </UFormField>
      <UFormField :label="t('schedules.form.kind')" name="kind" required>
        <USelect v-model="form.kind" :disabled="disabled || schedule !== null" :items="kindItems" />
      </UFormField>
      <UFormField :label="t('schedules.form.cron')" name="cronExpression" required>
        <UInput v-model="form.cronExpression" :disabled="disabled" placeholder="0 4 * * *" />
      </UFormField>
      <UFormField :label="t('schedules.form.timeZone')" name="timeZoneId" required>
        <UInput v-model="form.timeZoneId" :disabled="disabled" placeholder="UTC" />
      </UFormField>
      <UFormField :label="t('schedules.form.policy')" name="concurrencyPolicy" required>
        <USelect v-model="form.concurrencyPolicy" :disabled="disabled" :items="policyItems" />
      </UFormField>
      <UFormField :label="t('schedules.form.enabled')" name="enabled">
        <USwitch v-model="form.enabled" :disabled="disabled" />
      </UFormField>
    </div>

    <UFormField v-if="form.kind === 'ScheduledConsoleCommand'" :label="t('schedules.form.command')" name="commandText" required>
      <UInput v-model="form.commandText" :disabled="disabled" />
    </UFormField>
    <UFormField v-else-if="form.kind === 'ScheduledRestart'" :label="t('schedules.form.countdown')" name="countdownSeconds" required>
      <UInputNumber v-model="form.countdownSeconds" :disabled="disabled" :min="0" />
    </UFormField>
    <UFormField v-else :label="t('schedules.form.message')" name="messageText" required>
      <UTextarea v-model="form.messageText" autoresize :disabled="disabled" :maxlength="500" />
    </UFormField>

    <div class="flex justify-end gap-2">
      <UButton color="neutral" :disabled="disabled" :label="t('common.cancel')" type="button" variant="outline" @click="emit('cancel')" />
      <UButton :disabled="disabled" :label="schedule === null ? t('schedules.form.create') : t('common.save')" type="submit" />
    </div>
  </UForm>
</template>

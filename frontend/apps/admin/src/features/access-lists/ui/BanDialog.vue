<script setup lang="ts">
import type { BanEntry, BanInput } from '../api/accessLists'

import { reactive, watch } from 'vue'
import { useI18n } from 'vue-i18n'

const props = defineProps<{ entry: BanEntry | null }>()
const open = defineModel<boolean>('open', { required: true })
const emit = defineEmits<{ save: [input: BanInput] }>()
const { t } = useI18n()
const form = reactive({ playerId: '', displayName: '', bannedUntilUtc: '', reason: '' })

watch(() => [open.value, props.entry] as const, ([isOpen, entry]) => {
  if (!isOpen) return
  Object.assign(form, entry
    ? { ...entry, bannedUntilUtc: entry.bannedUntilUtc ?? '', reason: entry.reason ?? '' }
    : { playerId: '', displayName: '', bannedUntilUtc: '', reason: '' })
}, { immediate: true })

function submit() {
  emit('save', {
    playerId: form.playerId.trim(),
    displayName: form.displayName.trim(),
    bannedUntilUtc: form.bannedUntilUtc.trim() || null,
    reason: form.reason.trim() || null,
  })
}
</script>

<template>
  <UModal v-model:open="open" :title="t('accessLists.banDialog.title')">
    <template #body>
      <form class="space-y-3" @submit.prevent="submit">
        <UFormField :label="t('accessLists.fields.playerId')"><UInput v-model="form.playerId" :disabled="entry !== null" /></UFormField>
        <UFormField :label="t('accessLists.fields.displayName')"><UInput v-model="form.displayName" /></UFormField>
        <UFormField :label="t('accessLists.fields.bannedUntil')"><UInput v-model="form.bannedUntilUtc" type="datetime-local" /></UFormField>
        <UFormField :label="t('accessLists.fields.reason')"><UTextarea v-model="form.reason" :maxlength="200" /></UFormField>
        <p class="text-sm text-muted">{{ t('accessLists.banDialog.consequence') }}</p>
        <div class="flex justify-end gap-2"><UButton :label="t('common.cancel')" color="neutral" variant="outline" @click="open = false" /><UButton type="submit" :label="t('accessLists.action.confirmSave')" /></div>
      </form>
    </template>
  </UModal>
</template>

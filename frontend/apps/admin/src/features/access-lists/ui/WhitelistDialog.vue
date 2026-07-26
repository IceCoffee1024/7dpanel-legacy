<script setup lang="ts">
import type { WhitelistEntry, WhitelistInput } from '../api/accessLists'

import { reactive, watch } from 'vue'
import { useI18n } from 'vue-i18n'

const props = defineProps<{ entry: WhitelistEntry | null }>()
const open = defineModel<boolean>('open', { required: true })
const emit = defineEmits<{ save: [input: WhitelistInput] }>()
const { t } = useI18n()
const form = reactive<WhitelistInput>({ playerId: '', displayName: '' })

watch(() => [open.value, props.entry] as const, ([isOpen, entry]) => {
  if (isOpen)
    Object.assign(form, entry ?? { playerId: '', displayName: '' })
}, { immediate: true })

function submit() {
  emit('save', { playerId: form.playerId.trim(), displayName: form.displayName.trim() })
}
</script>

<template>
  <UModal v-model:open="open" :title="t('accessLists.whitelistDialog.title')">
    <template #body>
      <form class="space-y-3" @submit.prevent="submit">
        <UFormField :label="t('accessLists.fields.playerId')"><UInput v-model="form.playerId" :disabled="entry !== null" /></UFormField>
        <UFormField :label="t('accessLists.fields.displayName')"><UInput v-model="form.displayName" /></UFormField>
        <p class="text-sm text-muted">{{ t('accessLists.whitelistDialog.consequence') }}</p>
        <div class="flex justify-end gap-2"><UButton :label="t('common.cancel')" color="neutral" variant="outline" @click="open = false" /><UButton type="submit" :label="t('accessLists.action.confirmSave')" /></div>
      </form>
    </template>
  </UModal>
</template>

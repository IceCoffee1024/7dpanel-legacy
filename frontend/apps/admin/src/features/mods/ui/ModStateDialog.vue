<script setup lang="ts">
import type { ModMetadata } from '../api/mods'

import { useI18n } from 'vue-i18n'

const props = defineProps<{
  mod: ModMetadata | null
  enabled: boolean
  submitting: boolean
}>()
const emit = defineEmits<{ confirm: [] }>()
const open = defineModel<boolean>('open', { default: false })
const { t } = useI18n()
</script>

<template>
  <UModal v-model:open="open" :title="t('mods.dialog.title')">
    <template #body>
      <p class="text-sm text-muted">
        {{ t('mods.dialog.description', { name: props.mod?.displayName, state: t(props.enabled ? 'mods.action.enable' : 'mods.action.disable') }) }}
      </p>
      <p class="mt-2 text-sm text-warning">
        {{ t('mods.restartHint') }}
      </p>
    </template>
    <template #footer>
      <UButton
        color="neutral"
        :label="t('common.cancel')"
        variant="ghost"
        @click="open = false"
      />
      <UButton :loading="props.submitting" :label="t('common.confirm')" @click="emit('confirm')" />
    </template>
  </UModal>
</template>

<script setup lang="ts">
import { computed, reactive } from 'vue'
import { useI18n } from 'vue-i18n'

defineProps<{ disabled: boolean }>()
const emit = defineEmits<{ submit: [message: string] }>()
const { t } = useI18n()
const form = reactive({ message: '' })
const length = computed(() => Array.from(form.message.trim()).length)
const invalid = computed(() => length.value < 1 || length.value > 500)

function submit() {
  if (invalid.value)
    return
  emit('submit', form.message.trim())
}
</script>

<template>
  <UCard>
    <template #header>
      <div>
        <h2 class="font-semibold">
          {{ t('schedules.announcement.title') }}
        </h2>
        <p class="text-sm text-muted">
          {{ t('schedules.announcement.description') }}
        </p>
      </div>
    </template>
    <UForm class="space-y-3" :state="form" @submit="submit">
      <UFormField :hint="`${length}/500`" :label="t('schedules.announcement.message')" name="message">
        <UTextarea
          v-model="form.message"
          autoresize
          data-testid="announcement-message"
          :disabled="disabled"
          :maxlength="500"
          :placeholder="t('schedules.announcement.placeholder')"
          :rows="3"
        />
      </UFormField>
      <div class="flex justify-end">
        <UButton
          data-testid="send-announcement"
          :disabled="disabled || invalid"
          icon="i-lucide-megaphone"
          :label="t('schedules.announcement.send')"
          type="submit"
        />
      </div>
    </UForm>
  </UCard>
</template>

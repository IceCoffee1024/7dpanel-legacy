<script setup lang="ts">
import type { OnlinePlayer } from '../api/onlinePlayers'
import type { KickPlayerFeedback } from '../model/useKickPlayer'

import { computed, ref, watch } from 'vue'

const props = defineProps<{
  player: OnlinePlayer | null
  isSubmitting: boolean
  feedback: KickPlayerFeedback | null
}>()

const emit = defineEmits<{
  confirm: [reason: string]
  cancel: []
}>()
const open = defineModel<boolean>('open', { required: true })

const reason = ref('')
let activeTargetKey: string | null = null

const targetKey = computed(() => props.player === null
  ? null
  : `${props.player.entityId}:${props.player.platformIdentity.platform}:${props.player.platformIdentity.combinedId}`)
const trimmedReason = computed(() => reason.value.trim())
const canConfirm = computed(() => props.player !== null
  && !props.isSubmitting
  && trimmedReason.value.length >= 1
  && trimmedReason.value.length <= 200)
const controlledOpen = computed({
  get: () => open.value,
  set: (value: boolean) => {
    if (!value && props.isSubmitting)
      return
    open.value = value
    if (!value)
      emit('cancel')
  },
})

watch([open, targetKey], ([isOpen, currentTargetKey]) => {
  if (!isOpen || currentTargetKey === null || currentTargetKey === activeTargetKey)
    return
  activeTargetKey = currentTargetKey
  reason.value = ''
}, { immediate: true })

function cancel() {
  if (props.isSubmitting)
    return
  controlledOpen.value = false
}

function confirm() {
  if (!canConfirm.value)
    return
  emit('confirm', trimmedReason.value)
}
</script>

<template>
  <UModal
    v-model:open="controlledOpen"
    title="踢出玩家"
    description="确认目标身份并填写本次操作原因。"
    :dismissible="!isSubmitting"
    :close="isSubmitting ? false : undefined"
    :ui="{ footer: 'justify-end' }"
  >
    <template #body>
      <div v-if="player" class="space-y-5">
        <div class="min-w-0 rounded-md border border-default bg-elevated p-3">
          <p class="wrap-break-word font-medium text-highlighted">
            {{ player.name }}
          </p>
          <p class="mt-1 text-xs text-muted">
            {{ player.platformIdentity.platform }}
          </p>
          <code class="mt-1 block overflow-wrap-anywhere text-xs text-default">
            {{ player.platformIdentity.combinedId }}
          </code>
        </div>

        <UFormField
          label="踢出原因"
          name="kick-reason"
          required
          :hint="`${reason.length}/200`"
        >
          <UTextarea
            v-model="reason"
            :disabled="isSubmitting"
            :maxlength="200"
            :rows="4"
            class="w-full"
            placeholder="请输入将记录到审计日志的原因"
          />
        </UFormField>

        <p
          v-if="feedback"
          role="status"
          aria-live="polite"
          class="text-sm text-error"
        >
          {{ feedback.message }}
        </p>
      </div>
    </template>

    <template #footer>
      <UButton
        data-testid="cancel-kick-player"
        label="取消"
        color="neutral"
        variant="outline"
        :disabled="isSubmitting"
        @click="cancel"
      />
      <UButton
        data-testid="confirm-kick-player"
        label="踢出玩家"
        icon="i-lucide-log-out"
        color="error"
        :loading="isSubmitting"
        :disabled="!canConfirm"
        @click="confirm"
      />
    </template>
  </UModal>
</template>

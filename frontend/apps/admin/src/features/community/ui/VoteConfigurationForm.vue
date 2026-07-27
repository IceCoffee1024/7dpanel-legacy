<script setup lang="ts">
import type { VoteConfiguration, VoteConfigurationInput } from '../api/community'

import { computed, shallowRef, watch } from 'vue'
import { useI18n } from 'vue-i18n'

const props = defineProps<{
  configuration: VoteConfiguration
  saving: boolean
}>()

const emit = defineEmits<{ save: [current: VoteConfiguration, input: VoteConfigurationInput] }>()
const { t } = useI18n()

const enabled = shallowRef(false)
const durationMs = shallowRef('0')
const thresholdPercent = shallowRef(50)
const minimumParticipants = shallowRef(1)
const initiatorMinimumOnlineMs = shallowRef('0')
const participantMinimumOnlineMs = shallowRef('0')
const initiatorCooldownMs = shallowRef('0')
const targetCooldownMs = shallowRef('0')
const globalCooldownMs = shallowRef('0')
const mutualExclusionScope = shallowRef('')
const allowVoteChange = shallowRef(false)

function reset(configuration: VoteConfiguration) {
  enabled.value = configuration.enabled
  durationMs.value = configuration.durationMs.toString()
  thresholdPercent.value = configuration.thresholdPercent
  minimumParticipants.value = configuration.minimumParticipants
  initiatorMinimumOnlineMs.value = configuration.initiatorMinimumOnlineMs.toString()
  participantMinimumOnlineMs.value = configuration.participantMinimumOnlineMs.toString()
  initiatorCooldownMs.value = configuration.initiatorCooldownMs.toString()
  targetCooldownMs.value = configuration.targetCooldownMs.toString()
  globalCooldownMs.value = configuration.globalCooldownMs.toString()
  mutualExclusionScope.value = configuration.mutualExclusionScope
  allowVoteChange.value = configuration.allowVoteChange
}

watch(() => props.configuration, reset, { immediate: true })

const valid = computed(() => {
  const nonNegativeInteger = /^\d+$/
  return /^\d+$/.test(durationMs.value) && BigInt(durationMs.value) > 0n
    && [initiatorMinimumOnlineMs.value, participantMinimumOnlineMs.value, initiatorCooldownMs.value, targetCooldownMs.value, globalCooldownMs.value].every(value => nonNegativeInteger.test(value))
    && Number.isSafeInteger(thresholdPercent.value) && thresholdPercent.value >= 1 && thresholdPercent.value <= 100
    && Number.isSafeInteger(minimumParticipants.value) && minimumParticipants.value >= 1
    && mutualExclusionScope.value.trim() !== ''
})

function submit() {
  if (!valid.value)
    return
  emit('save', props.configuration, {
    enabled: enabled.value,
    durationMs: BigInt(durationMs.value),
    thresholdPercent: thresholdPercent.value,
    minimumParticipants: minimumParticipants.value,
    initiatorMinimumOnlineMs: BigInt(initiatorMinimumOnlineMs.value),
    participantMinimumOnlineMs: BigInt(participantMinimumOnlineMs.value),
    initiatorCooldownMs: BigInt(initiatorCooldownMs.value),
    targetCooldownMs: BigInt(targetCooldownMs.value),
    globalCooldownMs: BigInt(globalCooldownMs.value),
    mutualExclusionScope: mutualExclusionScope.value.trim(),
    allowVoteChange: allowVoteChange.value,
  })
}
</script>

<template>
  <UCard>
    <template #header>
      <div class="flex min-w-0 flex-wrap items-start justify-between gap-3">
        <div class="min-w-0">
          <h3 class="font-semibold text-highlighted">{{ configuration.kind }}</h3>
          <p class="text-xs text-muted">{{ t('community.voteConfiguration.version', { version: configuration.rowVersion.toString() }) }} · {{ configuration.updatedAtUtc }}</p>
        </div>
        <USwitch v-model="enabled" :label="t('community.common.enabled')" />
      </div>
    </template>

    <form class="grid gap-4 sm:grid-cols-2 xl:grid-cols-3" @submit.prevent="submit">
      <UFormField :label="t('community.voteConfiguration.durationMs')" required><UInput v-model="durationMs" class="w-full" inputmode="numeric" /></UFormField>
      <UFormField :label="t('community.voteConfiguration.thresholdPercent')" required><UInputNumber v-model="thresholdPercent" class="w-full" :min="1" :max="100" /></UFormField>
      <UFormField :label="t('community.voteConfiguration.minimumParticipants')" required><UInputNumber v-model="minimumParticipants" class="w-full" :min="1" /></UFormField>
      <UFormField :label="t('community.voteConfiguration.initiatorMinimumOnlineMs')" required><UInput v-model="initiatorMinimumOnlineMs" class="w-full" inputmode="numeric" /></UFormField>
      <UFormField :label="t('community.voteConfiguration.participantMinimumOnlineMs')" required><UInput v-model="participantMinimumOnlineMs" class="w-full" inputmode="numeric" /></UFormField>
      <UFormField :label="t('community.voteConfiguration.initiatorCooldownMs')" required><UInput v-model="initiatorCooldownMs" class="w-full" inputmode="numeric" /></UFormField>
      <UFormField :label="t('community.voteConfiguration.targetCooldownMs')" required><UInput v-model="targetCooldownMs" class="w-full" inputmode="numeric" /></UFormField>
      <UFormField :label="t('community.voteConfiguration.globalCooldownMs')" required><UInput v-model="globalCooldownMs" class="w-full" inputmode="numeric" /></UFormField>
      <UFormField :label="t('community.voteConfiguration.mutualExclusionScope')" required><UInput v-model="mutualExclusionScope" class="w-full" /></UFormField>
      <UFormField :label="t('community.voteConfiguration.voteSelection')">
        <USwitch v-model="allowVoteChange" :label="t('community.voteConfiguration.allowVoteChange')" />
      </UFormField>
    </form>

    <template #footer>
      <div class="flex flex-wrap justify-end gap-2">
        <UButton color="neutral" :label="t('community.common.restoreServerValue')" variant="outline" :disabled="saving" @click="reset(configuration)" />
        <UButton :label="t('community.common.saveAndConfirm')" :disabled="!valid" :loading="saving" @click="submit" />
      </div>
    </template>
  </UCard>
</template>

<script setup lang="ts">
import type { TeleportSettings, TeleportSettingsInput } from '../api/community'

import { computed, reactive, shallowRef, watch } from 'vue'
import { useI18n } from 'vue-i18n'

const props = defineProps<{
  setting: TeleportSettings
  saving: boolean
}>()

const emit = defineEmits<{ save: [current: TeleportSettings, input: TeleportSettingsInput] }>()
const { t } = useI18n()

const enabled = shallowRef(false)
const maxHomes = shallowRef<number | null>(null)
const cooldownMs = shallowRef('0')
const globalCooldownMs = shallowRef('0')
const denyDuringBloodMoon = shallowRef(false)
const feeAmount = shallowRef('0')
const setFeeAmount = shallowRef('0')
const homeExperience = reactive({
  listCommandName: '',
  setCommandName: '',
  deleteCommandName: '',
  teleportCommandName: '',
  noHomesMessage: '',
  limitMessage: '',
  setSuccessMessage: '',
  overwriteMessage: '',
  deleteSuccessMessage: '',
  notFoundMessage: '',
  cooldownMessage: '',
  teleportSuccessMessage: '',
  setInsufficientFundsMessage: '',
  teleportInsufficientFundsMessage: '',
  bloodMoonMessage: '',
})

const commandFields = [
  ['listCommandName', 'listCommandName'],
  ['setCommandName', 'setCommandName'],
  ['deleteCommandName', 'deleteCommandName'],
  ['teleportCommandName', 'teleportCommandName'],
] as const
const messageFields = [
  ['noHomesMessage', 'noHomesMessage'],
  ['limitMessage', 'limitMessage'],
  ['setSuccessMessage', 'setSuccessMessage'],
  ['overwriteMessage', 'overwriteMessage'],
  ['deleteSuccessMessage', 'deleteSuccessMessage'],
  ['notFoundMessage', 'notFoundMessage'],
  ['cooldownMessage', 'cooldownMessage'],
  ['teleportSuccessMessage', 'teleportSuccessMessage'],
  ['setInsufficientFundsMessage', 'setInsufficientFundsMessage'],
  ['teleportInsufficientFundsMessage', 'teleportInsufficientFundsMessage'],
  ['bloodMoonMessage', 'bloodMoonMessage'],
] as const

function reset(setting: TeleportSettings) {
  enabled.value = setting.enabled
  maxHomes.value = setting.maxHomes
  cooldownMs.value = setting.cooldownMs.toString()
  globalCooldownMs.value = setting.globalCooldownMs.toString()
  denyDuringBloodMoon.value = setting.denyDuringBloodMoon
  feeAmount.value = setting.feeAmount.toString()
  if (setting.homeExperience != null) {
    const { setFeeAmount: nextSetFeeAmount, ...nextHomeExperience } = setting.homeExperience
    setFeeAmount.value = nextSetFeeAmount.toString()
    Object.assign(homeExperience, nextHomeExperience)
  }
}

watch(() => props.setting, reset, { immediate: true })

const valid = computed(() => {
  const nonNegativeInteger = /^\d+$/
  const commandNames = commandFields.map(([field]) => homeExperience[field].trim())
  const validCommands = commandNames.every(value => value !== '' && !/\s/.test(value))
    && new Set(commandNames.map(value => value.toLocaleLowerCase())).size === commandNames.length
  return nonNegativeInteger.test(cooldownMs.value)
    && nonNegativeInteger.test(globalCooldownMs.value)
    && nonNegativeInteger.test(feeAmount.value)
    && (props.setting.kind !== 'Home' || (nonNegativeInteger.test(setFeeAmount.value)
      && validCommands
      && Object.values(homeExperience).every(value => value.trim() !== '')))
    && (props.setting.kind !== 'Home' || (maxHomes.value !== null && Number.isSafeInteger(maxHomes.value) && maxHomes.value >= 0))
})

function submit() {
  if (!valid.value)
    return
  emit('save', props.setting, {
    enabled: enabled.value,
    maxHomes: props.setting.kind === 'Home' ? maxHomes.value : null,
    cooldownMs: BigInt(cooldownMs.value),
    globalCooldownMs: BigInt(globalCooldownMs.value),
    denyDuringBloodMoon: denyDuringBloodMoon.value,
    feeAmount: BigInt(feeAmount.value),
    homeExperience: props.setting.kind === 'Home'
      ? { ...homeExperience, setFeeAmount: BigInt(setFeeAmount.value) }
      : null,
  })
}
</script>

<template>
  <UCard>
    <template #header>
      <div class="flex min-w-0 flex-wrap items-start justify-between gap-3">
        <div class="min-w-0">
          <h3 class="font-semibold text-highlighted">
            {{ setting.kind }}
          </h3>
          <p class="text-xs text-muted">
            {{ t('community.teleportSetting.version', { version: setting.rowVersion.toString() }) }} · {{ setting.updatedAtUtc }}
          </p>
        </div>
        <USwitch v-model="enabled" :label="t('community.common.enabled')" />
      </div>
    </template>

    <form class="grid gap-4 sm:grid-cols-2 xl:grid-cols-3" @submit.prevent="submit">
      <UFormField v-if="setting.kind === 'Home'" :label="t('community.teleportSetting.maxHomes')" required>
        <UInputNumber v-model="maxHomes" class="w-full" :min="0" />
      </UFormField>
      <UFormField :label="t('community.teleportSetting.cooldownMs')" required>
        <UInput v-model="cooldownMs" class="w-full" inputmode="numeric" />
      </UFormField>
      <UFormField :label="t('community.teleportSetting.globalCooldownMs')" required>
        <UInput v-model="globalCooldownMs" class="w-full" inputmode="numeric" />
      </UFormField>
      <UFormField :label="t('community.teleportSetting.feeAmount')" required>
        <UInput v-model="feeAmount" class="w-full" inputmode="numeric" />
      </UFormField>
      <template v-if="setting.kind === 'Home'">
        <UFormField :label="t('community.teleportSetting.setFeeAmount')" required>
          <UInput v-model="setFeeAmount" class="w-full" inputmode="numeric" />
        </UFormField>
        <UFormField
          v-for="field in commandFields"
          :key="field[0]"
          :label="t(`community.teleportSetting.${field[1]}`)"
          :description="t('community.teleportSetting.commandImmediateHint')"
          required
        >
          <UInput v-model="homeExperience[field[0]]" class="w-full" />
        </UFormField>
        <UFormField
          v-for="field in messageFields"
          :key="field[0]"
          class="sm:col-span-2 xl:col-span-3"
          :label="t(`community.teleportSetting.${field[1]}`)"
          required
        >
          <UInput v-model="homeExperience[field[0]]" class="w-full" />
        </UFormField>
      </template>
      <UFormField :label="t('community.teleportSetting.bloodMoonRestriction')">
        <USwitch v-model="denyDuringBloodMoon" :label="t('community.teleportSetting.denyDuringBloodMoon')" />
      </UFormField>
    </form>

    <template #footer>
      <div class="flex flex-wrap justify-end gap-2">
        <UButton
          color="neutral"
          :label="t('community.common.restoreServerValue')"
          variant="outline"
          :disabled="saving"
          @click="reset(setting)"
        />
        <UButton
          :label="t('community.common.saveAndConfirm')"
          :disabled="!valid"
          :loading="saving"
          @click="submit"
        />
      </div>
    </template>
  </UCard>
</template>

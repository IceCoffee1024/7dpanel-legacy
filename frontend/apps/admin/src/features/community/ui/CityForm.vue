<script setup lang="ts">
import type { City, CityInput } from '../api/community'

import { computed, reactive, watch } from 'vue'
import { useI18n } from 'vue-i18n'

const props = defineProps<{
  city: City | null
  saving: boolean
}>()
const emit = defineEmits<{
  save: [input: CityInput]
  cancel: []
}>()

const { t } = useI18n()

interface CityDraft {
  cityId: string
  name: string
  description: string
  enabled: boolean
  worldId: string
  x: number
  y: number
  z: number
  yaw: number
  sortOrder: number
}

const draft = reactive<CityDraft>({
  cityId: '',
  name: '',
  description: '',
  enabled: true,
  worldId: '',
  x: 0,
  y: 0,
  z: 0,
  yaw: 0,
  sortOrder: 0,
})

function reset(city: City | null) {
  draft.cityId = city?.cityId ?? ''
  draft.name = city?.name ?? ''
  draft.description = city?.description ?? ''
  draft.enabled = city?.enabled ?? true
  draft.worldId = city?.position.worldId ?? ''
  draft.x = city?.position.x ?? 0
  draft.y = city?.position.y ?? 0
  draft.z = city?.position.z ?? 0
  draft.yaw = city?.position.yaw ?? 0
  draft.sortOrder = city?.sortOrder ?? 0
}

watch(() => props.city, reset, { immediate: true })

const valid = computed(() => draft.cityId.trim() !== ''
  && draft.name.trim() !== ''
  && draft.worldId.trim() !== ''
  && [draft.x, draft.y, draft.z, draft.yaw].every(Number.isFinite)
  && Number.isSafeInteger(draft.sortOrder))

function submit() {
  if (!valid.value)
    return
  emit('save', {
    cityId: draft.cityId.trim(),
    name: draft.name.trim(),
    description: draft.description.trim(),
    enabled: draft.enabled,
    position: {
      worldId: draft.worldId.trim(),
      x: draft.x,
      y: draft.y,
      z: draft.z,
      yaw: draft.yaw,
    },
    sortOrder: draft.sortOrder,
  })
}
</script>

<template>
  <UCard>
    <template #header>
      <div>
        <h2 class="font-semibold text-highlighted">
          {{ city === null ? t('community.cityForm.createTitle') : t('community.cityForm.editTitle', { name: city.name }) }}
        </h2>
        <p class="text-sm text-muted">
          {{ t('community.cityForm.description') }}
        </p>
      </div>
    </template>

    <form class="grid gap-4 sm:grid-cols-2 xl:grid-cols-4" @submit.prevent="submit">
      <UFormField :label="t('community.cityForm.cityId')" required>
        <UInput v-model="draft.cityId" class="w-full" :disabled="city !== null" />
      </UFormField>
      <UFormField :label="t('community.cityForm.name')" required>
        <UInput v-model="draft.name" class="w-full" />
      </UFormField>
      <UFormField class="sm:col-span-2" :label="t('community.cityForm.descriptionField')">
        <UInput v-model="draft.description" class="w-full" />
      </UFormField>
      <UFormField :label="t('community.cityForm.worldId')" required>
        <UInput v-model="draft.worldId" class="w-full" />
      </UFormField>
      <UFormField label="X" required>
        <UInputNumber v-model="draft.x" class="w-full" />
      </UFormField>
      <UFormField label="Y" required>
        <UInputNumber v-model="draft.y" class="w-full" />
      </UFormField>
      <UFormField label="Z" required>
        <UInputNumber v-model="draft.z" class="w-full" />
      </UFormField>
      <UFormField :label="t('community.cityForm.yaw')" required>
        <UInputNumber v-model="draft.yaw" class="w-full" />
      </UFormField>
      <UFormField :label="t('community.cityForm.sortOrder')" required>
        <UInputNumber v-model="draft.sortOrder" class="w-full" />
      </UFormField>
      <UFormField :label="t('community.cityForm.availability')">
        <USwitch v-model="draft.enabled" :label="t('community.cityForm.playerSelectable')" />
      </UFormField>
    </form>

    <template #footer>
      <div class="flex flex-wrap justify-end gap-2">
        <UButton
          color="neutral"
          :label="t('community.common.clear')"
          variant="outline"
          :disabled="saving"
          @click="emit('cancel')"
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

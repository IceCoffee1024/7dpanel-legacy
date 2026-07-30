<script setup lang="ts">
import type { ServerConfigurationField } from '../api/serverConfiguration'

import { computed } from 'vue'

const props = defineProps<{
  field: ServerConfigurationField
}>()
const model = defineModel<string>({ required: true })

const booleanModel = computed({
  get: () => model.value === 'true',
  set: (value) => {
    model.value = value ? 'true' : 'false'
  },
})
const enumItems = computed(() => [...props.field.allowedValues])
</script>

<template>
  <UCheckbox
    v-if="props.field.valueType === 'boolean'"
    v-model="booleanModel"
    data-testid="boolean-editor"
  />
  <USelect
    v-else-if="props.field.valueType === 'enum'"
    v-model="model"
    class="w-full"
    data-testid="enum-editor"
    :items="enumItems"
  />
  <UInput
    v-else-if="props.field.valueType === 'integer' || props.field.valueType === 'decimal'"
    v-model="model"
    class="w-full"
    data-testid="scalar-editor"
    type="number"
    :min="props.field.minimum ?? undefined"
    :max="props.field.maximum ?? undefined"
    :step="props.field.valueType === 'integer' ? 1 : 'any'"
  />
  <UInput
    v-else
    v-model="model"
    class="w-full"
    data-testid="scalar-editor"
    type="text"
  />
</template>

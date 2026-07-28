<script setup lang="ts">
import type {
  CommunityGameCommandConfiguration,
  CommunityGameCommandConfigurationInput,
  CommunityGameCommandId,
} from '../api/community'

import { computed, reactive, watch } from 'vue'
import { useI18n } from 'vue-i18n'

const props = defineProps<{
  configuration: CommunityGameCommandConfiguration
  saving: boolean
}>()

const emit = defineEmits<{
  save: [current: CommunityGameCommandConfiguration, input: CommunityGameCommandConfigurationInput]
}>()
const { t } = useI18n()

interface EditableCommand {
  commandId: CommunityGameCommandId
  name: string
  aliasesText: string
}

const commands = reactive<EditableCommand[]>([])

function reset(configuration: CommunityGameCommandConfiguration) {
  commands.splice(0, commands.length, ...configuration.commands.map(command => ({
    commandId: command.commandId,
    name: command.name,
    aliasesText: command.aliases.join(', '),
  })))
}

function aliases(command: EditableCommand): string[] {
  return command.aliasesText
    .split(',')
    .map(value => value.trim())
    .filter(value => value !== '')
}

const valid = computed(() => {
  const tokens = ['help']
  for (const command of commands) {
    const values = [command.name.trim(), ...aliases(command)]
    if (values.some(value => value === '' || /\s/.test(value)))
      return false
    tokens.push(...values.map(value => value.toLocaleLowerCase()))
  }
  return new Set(tokens).size === tokens.length
})

function submit() {
  if (!valid.value)
    return
  emit('save', props.configuration, {
    commands: commands.map(command => ({
      commandId: command.commandId,
      name: command.name.trim(),
      aliases: aliases(command),
    })),
  })
}

watch(() => props.configuration, reset, { immediate: true })
</script>

<template>
  <UCard>
    <template #header>
      <div class="flex min-w-0 flex-wrap items-start justify-between gap-3">
        <div class="min-w-0">
          <h3 class="font-semibold text-highlighted">{{ t('community.gameCommands.title') }}</h3>
          <p class="text-sm text-muted">{{ t('community.gameCommands.description') }}</p>
        </div>
        <p class="text-xs text-muted">
          {{ t('community.gameCommands.version', { version: configuration.rowVersion.toString() }) }} · {{ configuration.updatedAtUtc }}
        </p>
      </div>
    </template>

    <form class="grid gap-4 md:grid-cols-2 xl:grid-cols-3" @submit.prevent="submit">
      <div v-for="command in commands" :key="command.commandId" class="rounded-lg border border-muted p-3">
        <p class="mb-3 text-sm font-medium text-highlighted">
          {{ t(`community.gameCommands.commandLabels.${command.commandId}`) }}
        </p>
        <div class="space-y-3">
          <UFormField
            :name="`${command.commandId}.name`"
            :label="t('community.gameCommands.name')"
            required
          >
            <UInput v-model="command.name" class="w-full" />
          </UFormField>
          <UFormField
            :name="`${command.commandId}.aliases`"
            :label="t('community.gameCommands.aliases')"
            :description="t('community.gameCommands.aliasesHint')"
          >
            <UInput v-model="command.aliasesText" class="w-full" />
          </UFormField>
        </div>
      </div>
    </form>

    <template #footer>
      <div class="flex flex-wrap items-center justify-between gap-3">
        <p class="text-sm text-muted">{{ t('community.gameCommands.immediateHint') }}</p>
        <div class="flex gap-2">
          <UButton
            color="neutral"
            :label="t('community.common.restoreServerValue')"
            variant="outline"
            :disabled="saving"
            @click="reset(configuration)"
          />
          <UButton
            :label="t('community.gameCommands.save')"
            :disabled="!valid"
            :loading="saving"
            @click="submit"
          />
        </div>
      </div>
    </template>
  </UCard>
</template>

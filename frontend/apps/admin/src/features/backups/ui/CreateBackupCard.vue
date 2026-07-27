<script setup lang="ts">
import type { BackupKind } from '../model/useBackups'

import { reactive } from 'vue'
import { useI18n } from 'vue-i18n'

defineProps<{ disabled: boolean }>()
const emit = defineEmits<{ create: [kind: BackupKind, worldName: string] }>()
const { t } = useI18n()
const form = reactive({ worldName: '' })
</script>

<template>
  <UCard>
    <template #header>
      <div>
        <h2 class="font-semibold">
          {{ t('backups.create.title') }}
        </h2>
        <p class="text-sm text-muted">
          {{ t('backups.create.description') }}
        </p>
      </div>
    </template>

    <div class="grid gap-4 lg:grid-cols-3">
      <div class="space-y-3 rounded-lg border border-default p-4">
        <UFormField :label="t('backups.create.worldName')" name="worldName">
          <UInput v-model="form.worldName" :disabled="disabled" :placeholder="t('backups.create.worldNamePlaceholder')" />
        </UFormField>
        <UButton
          block
          data-testid="create-world-backup"
          :disabled="disabled"
          icon="i-lucide-earth"
          :label="t('backups.create.world')"
          @click="emit('create', 'World', form.worldName)"
        />
      </div>

      <div class="flex flex-col justify-between gap-3 rounded-lg border border-default p-4">
        <p class="text-sm text-muted">
          {{ t('backups.create.panelDatabaseDescription') }}
        </p>
        <UButton
          block
          color="neutral"
          data-testid="create-panel-database-backup"
          :disabled="disabled"
          icon="i-lucide-database"
          :label="t('backups.create.panelDatabase')"
          variant="outline"
          @click="emit('create', 'PanelDatabase', '')"
        />
      </div>

      <div class="flex flex-col justify-between gap-3 rounded-lg border border-default p-4">
        <p class="text-sm text-muted">
          {{ t('backups.create.serverConfigurationDescription') }}
        </p>
        <UButton
          block
          color="neutral"
          data-testid="create-server-configuration-backup"
          :disabled="disabled"
          icon="i-lucide-file-cog"
          :label="t('backups.create.serverConfiguration')"
          variant="outline"
          @click="emit('create', 'ServerConfiguration', '')"
        />
      </div>
    </div>
  </UCard>
</template>

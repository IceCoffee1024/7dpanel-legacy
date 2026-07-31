<script setup lang="ts">
import type { TableColumn } from '@nuxt/ui'

import type { ChatMuteRecord, ChatMuteWriteInput, CreateChatMuteInput } from '../api/chatMutes'
import type { ChatMutesController } from '../model/useChatMutes'
import { computed, reactive, shallowRef } from 'vue'
import { useI18n } from 'vue-i18n'

const props = defineProps<{
  controller: ChatMutesController
}>()

const { t } = useI18n()
const columns = computed<TableColumn<ChatMuteRecord>[]>(() => [
  { accessorKey: 'crossplatformId', header: t('gameChat.mutes.table.crossplatformId') },
  { accessorKey: 'displayName', header: t('gameChat.mutes.table.displayName') },
  { accessorKey: 'reason', header: t('gameChat.mutes.table.reason') },
  { accessorKey: 'mutedUntilUtc', header: t('gameChat.mutes.table.expires') },
  { accessorKey: 'updatedAtUtc', header: t('gameChat.mutes.table.updatedAtUtc') },
  { id: 'actions', header: t('gameChat.mutes.table.actions') },
])
const mode = shallowRef<'create' | 'edit' | null>(null)
const editingId = shallowRef<string | null>(null)
const permanent = shallowRef(true)
const formError = shallowRef<string | null>(null)
const releaseTarget = shallowRef<ChatMuteRecord | null>(null)
const form = reactive({
  crossplatformId: '',
  displayName: '',
  reason: '',
  mutedUntilUtc: '',
  correlationId: '',
})
const formOpen = computed({
  get: () => mode.value !== null,
  set: (open: boolean) => {
    if (!open && !props.controller.isMutating.value)
      closeForm()
  },
})
const releaseOpen = computed({
  get: () => releaseTarget.value !== null,
  set: (open: boolean) => {
    if (!open && !props.controller.isMutating.value)
      releaseTarget.value = null
  },
})
const syntheticTotal = computed(() =>
  (props.controller.pageNumber.value + (props.controller.nextCursor.value === null ? 0 : 1)) * 50,
)
const tableData = computed(() => [...props.controller.mutes.value])

function resetForm() {
  Object.assign(form, {
    crossplatformId: '',
    displayName: '',
    reason: '',
    mutedUntilUtc: '',
    correlationId: '',
  })
  permanent.value = true
  formError.value = null
  editingId.value = null
}

function openCreate() {
  resetForm()
  mode.value = 'create'
}

function openEdit(record: ChatMuteRecord) {
  resetForm()
  mode.value = 'edit'
  editingId.value = record.crossplatformId
  Object.assign(form, {
    crossplatformId: record.crossplatformId,
    displayName: record.displayName ?? '',
    reason: record.reason,
    mutedUntilUtc: record.mutedUntilUtc ?? '',
    correlationId: '',
  })
  permanent.value = record.mutedUntilUtc === null
}

function closeForm() {
  mode.value = null
  resetForm()
}

function optional(value: string): string | null {
  const normalized = value.trim()
  return normalized === '' ? null : normalized
}

function isUtcTimestamp(value: string): boolean {
  return /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|\+00:00)$/.test(value)
    && Number.isFinite(Date.parse(value))
}

async function submitForm() {
  if (props.controller.isMutating.value)
    return
  const crossplatformId = form.crossplatformId.trim()
  const reason = form.reason.trim()
  const mutedUntilUtc = permanent.value ? null : form.mutedUntilUtc.trim()
  if (crossplatformId === '' || reason === '') {
    formError.value = t('gameChat.mutes.validation.required')
    return
  }
  if (mutedUntilUtc !== null && !isUtcTimestamp(mutedUntilUtc)) {
    formError.value = t('gameChat.mutes.validation.utcTimestamp')
    return
  }
  formError.value = null
  const input: ChatMuteWriteInput = {
    displayName: optional(form.displayName),
    reason,
    mutedUntilUtc,
    correlationId: optional(form.correlationId),
  }
  const succeeded = mode.value === 'create'
    ? await props.controller.create({ crossplatformId, ...input } satisfies CreateChatMuteInput)
    : await props.controller.update(editingId.value!, input)
  if (succeeded)
    closeForm()
}

async function confirmRelease() {
  const target = releaseTarget.value
  if (target === null || props.controller.isMutating.value)
    return
  const succeeded = await props.controller.release(target.crossplatformId, null)
  if (succeeded)
    releaseTarget.value = null
}
</script>

<template>
  <UDashboardPanel id="chat-mutes">
    <template #header>
      <UDashboardNavbar :title="t('gameChat.mutes.title')">
        <template #leading>
          <UDashboardSidebarCollapse />
        </template>
        <template #right>
          <UButton
            data-testid="create-mute-button"
            :disabled="controller.isMutating.value"
            icon="i-lucide-volume-x"
            :label="t('gameChat.mutes.create')"
            @click="openCreate"
          />
        </template>
      </UDashboardNavbar>
    </template>

    <template #body>
      <div class="space-y-4">
        <UAlert
          v-if="controller.state.value === 'stale'"
          color="warning"
          icon="i-lucide-triangle-alert"
          :title="t('gameChat.mutes.state.stale')"
        />
        <UAlert
          v-else-if="controller.state.value === 'forbidden'"
          color="warning"
          icon="i-lucide-shield-alert"
          :title="t('gameChat.mutes.state.forbidden')"
        />
        <UAlert
          v-else-if="controller.state.value === 'failed'"
          color="error"
          icon="i-lucide-circle-x"
          :title="t('gameChat.mutes.state.failed')"
        >
          <template #actions>
            <UButton
              color="neutral"
              :label="t('gameChat.common.retry')"
              variant="outline"
              @click="controller.retry"
            />
          </template>
        </UAlert>

        <div
          v-if="controller.state.value === 'loading'"
          class="space-y-2"
          role="status"
          :aria-label="t('gameChat.mutes.state.loading')"
        >
          <USkeleton v-for="row in 5" :key="row" class="h-10 w-full" />
        </div>

        <UAlert
          v-if="controller.state.value === 'ready' && controller.mutes.value.length === 0"
          color="neutral"
          :title="t('gameChat.mutes.state.empty')"
        />

        <div
          v-if="controller.mutes.value.length > 0"
          data-testid="mute-desktop-table"
          class="hidden overflow-x-auto rounded-lg border border-default md:block"
        >
          <UTable :aria-label="t('gameChat.mutes.table.aria')" :columns="columns" :data="tableData">
            <template #crossplatformId-cell="{ row }">
              <code>{{ row.original.crossplatformId }}</code>
            </template>
            <template #displayName-cell="{ row }">
              {{ row.original.displayName ?? '—' }}
            </template>
            <template #mutedUntilUtc-cell="{ row }">
              <UBadge
                v-if="row.original.mutedUntilUtc === null"
                color="error"
                :label="t('gameChat.mutes.permanent')"
                variant="subtle"
              />
              <time v-else class="whitespace-nowrap">{{ row.original.mutedUntilUtc }}</time>
            </template>
            <template #updatedAtUtc-cell="{ row }">
              <time class="whitespace-nowrap">{{ row.original.updatedAtUtc }}</time>
            </template>
            <template #actions-cell="{ row }">
              <div class="flex gap-2">
                <UButton
                  :aria-label="t('gameChat.mutes.actions.editAria', { id: row.original.crossplatformId })"
                  :data-testid="`edit-mute-desktop-${row.original.crossplatformId}`"
                  :disabled="controller.isMutating.value"
                  :label="t('gameChat.common.edit')"
                  size="xs"
                  variant="outline"
                  @click="openEdit(row.original)"
                />
                <UButton
                  :aria-label="t('gameChat.mutes.actions.releaseAria', { id: row.original.crossplatformId })"
                  :data-testid="`release-mute-desktop-${row.original.crossplatformId}`"
                  :disabled="controller.isMutating.value"
                  color="error"
                  :label="t('gameChat.mutes.actions.release')"
                  size="xs"
                  variant="soft"
                  @click="releaseTarget = row.original"
                />
              </div>
            </template>
          </UTable>
        </div>

        <ul
          v-if="controller.mutes.value.length > 0"
          data-testid="mute-mobile-list"
          class="grid gap-3 md:hidden"
          :aria-label="t('gameChat.mutes.cards.aria')"
        >
          <li
            v-for="record in controller.mutes.value"
            :key="record.crossplatformId"
            class="space-y-3 rounded-lg border border-default p-4"
          >
            <div>
              <p class="font-medium">
                {{ record.displayName ?? t('gameChat.mutes.cards.missingDisplayName') }}
              </p>
              <code class="break-all text-xs text-muted">{{ record.crossplatformId }}</code>
            </div>
            <p class="text-sm">
              {{ record.reason }}
            </p>
            <p class="text-sm text-muted">
              {{ record.mutedUntilUtc === null ? t('gameChat.mutes.permanent') : record.mutedUntilUtc }}
            </p>
            <div class="flex gap-2">
              <UButton
                :aria-label="t('gameChat.mutes.actions.editAria', { id: record.crossplatformId })"
                :data-testid="`edit-mute-mobile-${record.crossplatformId}`"
                :disabled="controller.isMutating.value"
                :label="t('gameChat.common.edit')"
                size="sm"
                variant="outline"
                @click="openEdit(record)"
              />
              <UButton
                :aria-label="t('gameChat.mutes.actions.releaseAria', { id: record.crossplatformId })"
                :data-testid="`release-mute-mobile-${record.crossplatformId}`"
                :disabled="controller.isMutating.value"
                color="error"
                :label="t('gameChat.mutes.actions.release')"
                size="sm"
                variant="soft"
                @click="releaseTarget = record"
              />
            </div>
          </li>
        </ul>

        <div v-if="controller.mutes.value.length > 0" class="flex justify-end">
          <UPagination
            :items-per-page="50"
            :page="controller.pageNumber.value"
            :total="syntheticTotal"
            @update:page="controller.goToPage"
          />
        </div>
      </div>
    </template>
  </UDashboardPanel>

  <UModal
    v-model:open="formOpen"
    :description="mode === 'create' ? t('gameChat.mutes.form.createDescription') : t('gameChat.mutes.form.editDescription')"
    :title="mode === 'create' ? t('gameChat.mutes.form.createTitle') : t('gameChat.mutes.form.editTitle')"
    :ui="{ footer: 'justify-end' }"
  >
    <template #body>
      <UForm
        id="chat-mute-form"
        :state="form"
        class="space-y-4"
        @submit="submitForm"
      >
        <UFormField :label="t('gameChat.mutes.form.crossplatformId')" name="crossplatformId" required>
          <UInput
            v-model="form.crossplatformId"
            data-testid="mute-crossplatform-id"
            class="w-full"
            :disabled="mode === 'edit'"
          />
        </UFormField>
        <UFormField :label="t('gameChat.mutes.form.displayName')" name="displayName">
          <UInput v-model="form.displayName" class="w-full" />
        </UFormField>
        <UFormField :label="t('gameChat.mutes.form.reason')" name="reason" required>
          <UTextarea
            v-model="form.reason"
            data-testid="mute-reason"
            autoresize
            class="w-full"
            :maxrows="6"
          />
        </UFormField>
        <UCheckbox v-model="permanent" :label="t('gameChat.mutes.form.permanent')" />
        <UFormField
          v-if="!permanent"
          :label="t('gameChat.mutes.form.expiresAtUtc')"
          name="mutedUntilUtc"
          required
        >
          <UInput v-model="form.mutedUntilUtc" class="w-full" placeholder="2026-07-26T08:00:00Z" />
        </UFormField>
        <UFormField :label="t('gameChat.mutes.form.correlationId')" name="correlationId" :hint="t('gameChat.common.optional')">
          <UInput v-model="form.correlationId" class="w-full" />
        </UFormField>
        <p v-if="formError" role="alert" class="text-sm text-error">
          {{ formError }}
        </p>
      </UForm>
    </template>
    <template #footer>
      <UButton
        :disabled="controller.isMutating.value"
        color="neutral"
        :label="t('gameChat.common.cancel')"
        variant="outline"
        @click="closeForm"
      />
      <UButton
        form="chat-mute-form"
        :disabled="controller.isMutating.value"
        data-testid="submit-mute-button"
        :label="mode === 'create' ? t('gameChat.mutes.form.create') : t('gameChat.mutes.form.save')"
        :loading="controller.isMutating.value"
        type="submit"
      />
    </template>
  </UModal>

  <UModal
    v-model:open="releaseOpen"
    :description="t('gameChat.mutes.release.description')"
    :title="t('gameChat.mutes.release.title')"
    :ui="{ footer: 'justify-end' }"
  >
    <template #body>
      <p id="chat-mute-release-confirmation">
        {{ t('gameChat.mutes.release.confirmation', { id: releaseTarget?.crossplatformId }) }}
      </p>
    </template>
    <template #footer>
      <UButton
        :disabled="controller.isMutating.value"
        color="neutral"
        :label="t('gameChat.common.cancel')"
        variant="outline"
        @click="releaseTarget = null"
      />
      <UButton
        :disabled="controller.isMutating.value"
        color="error"
        :aria-label="t('gameChat.mutes.release.confirmAria', { id: releaseTarget?.crossplatformId })"
        data-testid="confirm-release-mute"
        :label="t('gameChat.mutes.release.confirm')"
        :loading="controller.isMutating.value"
        @click="confirmRelease"
      />
    </template>
  </UModal>
</template>

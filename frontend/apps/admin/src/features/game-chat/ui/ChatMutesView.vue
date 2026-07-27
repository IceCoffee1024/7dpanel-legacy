<script setup lang="ts">
import type { TableColumn } from '@nuxt/ui'

import type { ChatMuteRecord, ChatMuteWriteInput, CreateChatMuteInput } from '../api/chatMutes'
import type { ChatMutesController } from '../model/useChatMutes'
import { computed, reactive, shallowRef } from 'vue'

const props = defineProps<{
  controller: ChatMutesController
}>()

const columns: TableColumn<ChatMuteRecord>[] = [
  { accessorKey: 'crossplatformId', header: '跨平台身份' },
  { accessorKey: 'displayName', header: '显示名' },
  { accessorKey: 'reason', header: '原因' },
  { accessorKey: 'mutedUntilUtc', header: '期限' },
  { accessorKey: 'updatedAtUtc', header: '更新时间（UTC）' },
  { id: 'actions', header: '操作' },
]
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
    formError.value = '跨平台身份和原因不能为空'
    return
  }
  if (mutedUntilUtc !== null && !isUtcTimestamp(mutedUntilUtc)) {
    formError.value = '期限必须是 UTC 时间，例如 2026-07-26T08:00:00Z'
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
      <UDashboardNavbar title="禁言管理">
        <template #leading>
          <UDashboardSidebarCollapse />
        </template>
        <template #right>
          <UButton
            data-testid="create-mute-button"
            :disabled="controller.isMutating.value"
            icon="i-lucide-volume-x"
            label="新增禁言"
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
          title="刷新失败，当前显示上一次成功结果"
        />
        <UAlert
          v-else-if="controller.state.value === 'forbidden'"
          color="warning"
          icon="i-lucide-shield-alert"
          title="当前账号无权管理禁言"
        />
        <UAlert
          v-else-if="controller.state.value === 'failed'"
          color="error"
          icon="i-lucide-circle-x"
          title="禁言列表加载失败"
        >
          <template #actions>
            <UButton
              color="neutral"
              label="重试"
              variant="outline"
              @click="controller.retry"
            />
          </template>
        </UAlert>

        <div v-if="controller.state.value === 'loading'" class="space-y-2" aria-label="正在加载禁言列表">
          <USkeleton v-for="row in 5" :key="row" class="h-10 w-full" />
        </div>

        <UAlert
          v-if="controller.state.value === 'ready' && controller.mutes.value.length === 0"
          color="neutral"
          title="当前没有生效中的禁言"
        />

        <div v-if="controller.mutes.value.length > 0" class="hidden overflow-x-auto rounded-lg border border-default md:block">
          <UTable :columns="columns" :data="tableData">
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
                label="永久"
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
                  :data-testid="`edit-mute-${row.original.crossplatformId}`"
                  :disabled="controller.isMutating.value"
                  label="编辑"
                  size="xs"
                  variant="outline"
                  @click="openEdit(row.original)"
                />
                <UButton
                  :data-testid="`release-mute-${row.original.crossplatformId}`"
                  :disabled="controller.isMutating.value"
                  color="error"
                  label="解除"
                  size="xs"
                  variant="soft"
                  @click="releaseTarget = row.original"
                />
              </div>
            </template>
          </UTable>
        </div>

        <div class="grid gap-3 md:hidden">
          <article
            v-for="record in controller.mutes.value"
            :key="record.crossplatformId"
            class="space-y-3 rounded-lg border border-default p-4"
          >
            <div>
              <p class="font-medium">
                {{ record.displayName ?? '未提供显示名' }}
              </p>
              <code class="break-all text-xs text-muted">{{ record.crossplatformId }}</code>
            </div>
            <p class="text-sm">
              {{ record.reason }}
            </p>
            <p class="text-sm text-muted">
              {{ record.mutedUntilUtc === null ? '永久' : record.mutedUntilUtc }}
            </p>
            <div class="flex gap-2">
              <UButton
                :data-testid="`edit-mute-${record.crossplatformId}`"
                :disabled="controller.isMutating.value"
                label="编辑"
                size="sm"
                variant="outline"
                @click="openEdit(record)"
              />
              <UButton
                :data-testid="`release-mute-${record.crossplatformId}`"
                :disabled="controller.isMutating.value"
                color="error"
                label="解除"
                size="sm"
                variant="soft"
                @click="releaseTarget = record"
              />
            </div>
          </article>
        </div>

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
    :description="mode === 'create' ? '创建永久或有明确 UTC 截止时间的禁言。' : '更新当前禁言并立即应用。'"
    :title="mode === 'create' ? '新增禁言' : '编辑禁言'"
    :ui="{ footer: 'justify-end' }"
  >
    <template #body>
      <UForm
        id="chat-mute-form"
        :state="form"
        class="space-y-4"
        @submit="submitForm"
      >
        <UFormField label="跨平台身份" name="crossplatformId" required>
          <UInput v-model="form.crossplatformId" class="w-full" :disabled="mode === 'edit'" />
        </UFormField>
        <UFormField label="显示名" name="displayName">
          <UInput v-model="form.displayName" class="w-full" />
        </UFormField>
        <UFormField label="原因" name="reason" required>
          <UTextarea
            v-model="form.reason"
            autoresize
            class="w-full"
            :maxrows="6"
          />
        </UFormField>
        <UCheckbox v-model="permanent" label="永久禁言" />
        <UFormField
          v-if="!permanent"
          label="截止时间（UTC）"
          name="mutedUntilUtc"
          required
        >
          <UInput v-model="form.mutedUntilUtc" class="w-full" placeholder="2026-07-26T08:00:00Z" />
        </UFormField>
        <UFormField label="关联 ID" name="correlationId" hint="可选">
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
        label="取消"
        variant="outline"
        @click="closeForm"
      />
      <UButton
        form="chat-mute-form"
        :disabled="controller.isMutating.value"
        :label="mode === 'create' ? '创建' : '保存'"
        :loading="controller.isMutating.value"
        type="submit"
      />
    </template>
  </UModal>

  <UModal
    v-model:open="releaseOpen"
    description="解除后该玩家可立即恢复发送聊天消息。"
    title="解除禁言"
    :ui="{ footer: 'justify-end' }"
  >
    <template #body>
      <p>确认解除 <code>{{ releaseTarget?.crossplatformId }}</code> 的禁言？</p>
    </template>
    <template #footer>
      <UButton
        :disabled="controller.isMutating.value"
        color="neutral"
        label="取消"
        variant="outline"
        @click="releaseTarget = null"
      />
      <UButton
        :disabled="controller.isMutating.value"
        color="error"
        label="确认解除"
        :loading="controller.isMutating.value"
        @click="confirmRelease"
      />
    </template>
  </UModal>
</template>

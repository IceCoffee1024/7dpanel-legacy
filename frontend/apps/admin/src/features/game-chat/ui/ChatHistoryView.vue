<script setup lang="ts">
import type { TableColumn } from '@nuxt/ui'
import type {
  ChatHistoryFilters,
  ChatHistoryMessage,
  GameChatManagementState,
} from '../model/gameChatManagement'

import { computed, reactive, watch } from 'vue'
import { useI18n } from 'vue-i18n'

import {
  chatSourceOptions,
  chatTypeOptions,
  createEmptyHistoryFilters,
} from '../model/gameChatManagement'

const props = defineProps<{
  state: GameChatManagementState
  messages: readonly ChatHistoryMessage[]
  filters: ChatHistoryFilters
  nextCursor: string | null
  isLoadingMore: boolean
}>()

const emit = defineEmits<{
  applyFilters: [filters: ChatHistoryFilters]
  loadMore: []
  retry: []
}>()
const { t } = useI18n()

const draft = reactive<ChatHistoryFilters>(createEmptyHistoryFilters())
const tableData = computed(() => [...props.messages])
const chatTypeSelectItems = computed(() => chatTypeOptions.map(value => ({
  label: t(`gameChat.channels.${value}`),
  value,
})))
const chatSourceSelectItems = computed(() => chatSourceOptions.map(value => ({
  label: t(`gameChat.sources.${value}`),
  value,
})))
const columns = computed<TableColumn<ChatHistoryMessage>[]>(() => [
  { accessorKey: 'occurredAtUtc', header: t('gameChat.history.table.occurredAtUtc') },
  { accessorKey: 'senderName', header: t('gameChat.history.table.senderName') },
  { accessorKey: 'crossplatformId', header: t('gameChat.common.crossplatformId') },
  { accessorKey: 'entityId', header: t('gameChat.common.entityId') },
  { accessorKey: 'chatType', header: t('gameChat.history.table.channel') },
  { accessorKey: 'sourceKind', header: t('gameChat.history.table.source') },
  { accessorKey: 'message', header: t('gameChat.history.table.message') },
])

watch(() => props.filters, (filters) => {
  Object.assign(draft, createEmptyHistoryFilters(), filters)
}, { immediate: true, deep: true })

function applyFilters() {
  emit('applyFilters', {
    ...draft,
    crossplatformId: draft.crossplatformId.trim(),
    senderName: draft.senderName.trim(),
    startUtc: draft.startUtc.trim(),
    endUtc: draft.endUtc.trim(),
  })
}
</script>

<template>
  <section class="space-y-4" aria-labelledby="chat-history-title">
    <header>
      <h1 id="chat-history-title" class="text-lg font-semibold text-highlighted">
        {{ t('gameChat.history.title') }}
      </h1>
      <p class="text-sm text-muted">
        {{ t('gameChat.history.description') }}
      </p>
    </header>

    <UForm :state="draft" class="grid gap-3 md:grid-cols-2 xl:grid-cols-4" @submit="applyFilters">
      <UFormField :label="t('gameChat.common.crossplatformId')" name="crossplatformId">
        <UInput v-model="draft.crossplatformId" data-testid="history-crossplatform-id" class="w-full" />
      </UFormField>
      <UFormField :label="t('gameChat.history.filters.senderName')" name="senderName">
        <UInput v-model="draft.senderName" data-testid="history-sender-name" class="w-full" />
      </UFormField>
      <UFormField :label="t('gameChat.history.filters.channel')" name="chatType">
        <USelect
          :model-value="draft.chatType || undefined"
          :items="chatTypeSelectItems"
          :placeholder="t('gameChat.history.filters.allChannels')"
          class="w-full"
          @update:model-value="draft.chatType = $event ?? ''"
        />
      </UFormField>
      <UFormField :label="t('gameChat.history.filters.source')" name="sourceKind">
        <USelect
          :model-value="draft.sourceKind || undefined"
          :items="chatSourceSelectItems"
          :placeholder="t('gameChat.history.filters.allSources')"
          class="w-full"
          @update:model-value="draft.sourceKind = $event ?? ''"
        />
      </UFormField>
      <UFormField :label="t('gameChat.history.filters.startUtc')" name="startUtc">
        <UInput v-model="draft.startUtc" type="datetime-local" class="w-full" />
      </UFormField>
      <UFormField :label="t('gameChat.history.filters.endUtc')" name="endUtc">
        <UInput v-model="draft.endUtc" type="datetime-local" class="w-full" />
      </UFormField>
      <div class="flex items-end md:col-span-2">
        <UButton type="submit" icon="i-lucide-search" :label="t('gameChat.history.filters.apply')" />
      </div>
    </UForm>

    <UAlert
      v-if="state === 'stale'"
      color="warning"
      :title="t('gameChat.common.staleTitle')"
      :description="t('gameChat.history.state.staleDescription')"
    />
    <div v-if="state === 'loading'" class="space-y-3" :aria-label="t('gameChat.history.state.loading')">
      <USkeleton v-for="row in 5" :key="row" class="h-14 w-full" />
    </div>
    <UAlert
      v-else-if="state === 'failed' || state === 'forbidden'"
      :color="state === 'forbidden' ? 'warning' : 'error'"
      :title="state === 'forbidden' ? t('gameChat.history.state.forbidden') : t('gameChat.history.state.failed')"
    >
      <template #actions>
        <UButton v-if="state === 'failed'" color="neutral" variant="outline" :label="t('gameChat.common.retry')" @click="emit('retry')" />
      </template>
    </UAlert>
    <div v-else-if="state === 'empty'" class="rounded-lg border border-dashed border-default py-12 text-center text-sm text-muted">
      {{ t('gameChat.history.state.empty') }}
    </div>

    <template v-else-if="state === 'ready' || state === 'stale'">
      <div class="hidden overflow-x-auto md:block">
        <UTable :columns="columns" :data="tableData">
          <template #occurredAtUtc-cell="{ row }">
            <time class="whitespace-nowrap text-sm">{{ row.original.occurredAtUtc }}</time>
          </template>
          <template #senderName-cell="{ row }">
            <span>{{ row.original.senderName ?? t('gameChat.sources.System') }}</span>
          </template>
          <template #crossplatformId-cell="{ row }">
            <code class="break-all text-xs">{{ row.original.crossplatformId ?? '—' }}</code>
          </template>
          <template #message-cell="{ row }">
            <p class="max-w-xl whitespace-pre-wrap wrap-break-word">{{ row.original.message }}</p>
          </template>
        </UTable>
      </div>

      <ul class="divide-y divide-default rounded-lg border border-default px-4 md:hidden">
        <li v-for="message in messages" :key="message.sequence" class="space-y-3 py-4">
          <div class="flex flex-wrap items-center justify-between gap-2">
            <strong class="wrap-break-word">{{ message.senderName ?? t('gameChat.sources.System') }}</strong>
            <time class="text-xs text-muted">{{ message.occurredAtUtc }}</time>
          </div>
          <div class="flex flex-wrap gap-2 text-xs">
            <UBadge color="neutral" variant="subtle">{{ t(`gameChat.channels.${message.chatType}`) }}</UBadge>
            <UBadge color="neutral" variant="outline">{{ t(`gameChat.sources.${message.sourceKind}`) }}</UBadge>
          </div>
          <dl class="grid gap-1 text-xs text-muted">
            <div><dt class="inline">{{ t('gameChat.common.crossplatformId') }}：</dt><dd class="inline break-all">{{ message.crossplatformId ?? '—' }}</dd></div>
            <div><dt class="inline">{{ t('gameChat.common.entityId') }}：</dt><dd class="inline">{{ message.entityId }}</dd></div>
          </dl>
          <p class="whitespace-pre-wrap wrap-break-word text-sm text-default">{{ message.message }}</p>
        </li>
      </ul>

      <div v-if="nextCursor" class="flex justify-center">
        <UButton
          data-testid="history-load-more"
          color="neutral"
          variant="outline"
          icon="i-lucide-chevron-down"
          :label="t('gameChat.common.loadMore')"
          :loading="isLoadingMore"
          :disabled="isLoadingMore"
          @click="emit('loadMore')"
        />
      </div>
    </template>
  </section>
</template>

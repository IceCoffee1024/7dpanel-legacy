<script setup lang="ts">
import type { TableColumn } from '@nuxt/ui'
import type {
  ChatHistoryFilters,
  ChatHistoryMessage,
  GameChatManagementState,
} from '../model/gameChatManagement'

import { computed, reactive, watch } from 'vue'

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

const draft = reactive<ChatHistoryFilters>(createEmptyHistoryFilters())
const tableData = computed(() => [...props.messages])
const chatTypeSelectItems = [...chatTypeOptions]
const chatSourceSelectItems = [...chatSourceOptions]
const columns: TableColumn<ChatHistoryMessage>[] = [
  { accessorKey: 'occurredAtUtc', header: '时间（UTC）' },
  { accessorKey: 'senderName', header: '发送者' },
  { accessorKey: 'crossplatformId', header: '跨平台 ID' },
  { accessorKey: 'entityId', header: '实体 ID' },
  { accessorKey: 'chatType', header: '频道' },
  { accessorKey: 'sourceKind', header: '来源' },
  { accessorKey: 'message', header: '正文' },
]

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
        历史聊天
      </h1>
      <p class="text-sm text-muted">
        按稳定身份、频道、来源和 UTC 时间范围筛选历史记录。
      </p>
    </header>

    <UForm :state="draft" class="grid gap-3 md:grid-cols-2 xl:grid-cols-4" @submit="applyFilters">
      <UFormField label="跨平台 ID" name="crossplatformId">
        <UInput v-model="draft.crossplatformId" data-testid="history-crossplatform-id" class="w-full" />
      </UFormField>
      <UFormField label="发送者名称" name="senderName">
        <UInput v-model="draft.senderName" data-testid="history-sender-name" class="w-full" />
      </UFormField>
      <UFormField label="频道" name="chatType">
        <USelect
          :model-value="draft.chatType || undefined"
          :items="chatTypeSelectItems"
          placeholder="全部频道"
          class="w-full"
          @update:model-value="draft.chatType = $event ?? ''"
        />
      </UFormField>
      <UFormField label="来源" name="sourceKind">
        <USelect
          :model-value="draft.sourceKind || undefined"
          :items="chatSourceSelectItems"
          placeholder="全部来源"
          class="w-full"
          @update:model-value="draft.sourceKind = $event ?? ''"
        />
      </UFormField>
      <UFormField label="开始时间（UTC）" name="startUtc">
        <UInput v-model="draft.startUtc" type="datetime-local" class="w-full" />
      </UFormField>
      <UFormField label="结束时间（UTC）" name="endUtc">
        <UInput v-model="draft.endUtc" type="datetime-local" class="w-full" />
      </UFormField>
      <div class="flex items-end md:col-span-2">
        <UButton type="submit" icon="i-lucide-search" label="应用筛选" />
      </div>
    </UForm>

    <UAlert
      v-if="state === 'stale'"
      color="warning"
      title="当前显示上次成功结果"
      description="最新刷新失败；筛选结果可能已过期。"
    />
    <div v-if="state === 'loading'" class="space-y-3" aria-label="正在加载历史聊天">
      <USkeleton v-for="row in 5" :key="row" class="h-14 w-full" />
    </div>
    <UAlert
      v-else-if="state === 'failed' || state === 'forbidden'"
      :color="state === 'forbidden' ? 'warning' : 'error'"
      :title="state === 'forbidden' ? '无权查看聊天历史' : '聊天历史加载失败'"
    >
      <template #actions>
        <UButton v-if="state === 'failed'" color="neutral" variant="outline" label="重试" @click="emit('retry')" />
      </template>
    </UAlert>
    <div v-else-if="state === 'empty'" class="rounded-lg border border-dashed border-default py-12 text-center text-sm text-muted">
      没有符合条件的聊天记录
    </div>

    <template v-else-if="state === 'ready' || state === 'stale'">
      <div class="hidden overflow-x-auto md:block">
        <UTable :columns="columns" :data="tableData">
          <template #occurredAtUtc-cell="{ row }">
            <time class="whitespace-nowrap text-sm">{{ row.original.occurredAtUtc }}</time>
          </template>
          <template #senderName-cell="{ row }">
            <span>{{ row.original.senderName ?? '系统' }}</span>
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
            <strong class="wrap-break-word">{{ message.senderName ?? '系统' }}</strong>
            <time class="text-xs text-muted">{{ message.occurredAtUtc }}</time>
          </div>
          <div class="flex flex-wrap gap-2 text-xs">
            <UBadge color="neutral" variant="subtle">{{ message.chatType }}</UBadge>
            <UBadge color="neutral" variant="outline">{{ message.sourceKind }}</UBadge>
          </div>
          <dl class="grid gap-1 text-xs text-muted">
            <div><dt class="inline">跨平台 ID：</dt><dd class="inline break-all">{{ message.crossplatformId ?? '—' }}</dd></div>
            <div><dt class="inline">实体 ID：</dt><dd class="inline">{{ message.entityId }}</dd></div>
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
          label="继续加载"
          :loading="isLoadingMore"
          :disabled="isLoadingMore"
          @click="emit('loadMore')"
        />
      </div>
    </template>
  </section>
</template>

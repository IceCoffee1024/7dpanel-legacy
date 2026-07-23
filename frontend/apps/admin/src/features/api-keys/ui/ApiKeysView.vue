<script setup lang="ts">
import type { ApiKeyMetadata, CreateApiKeyInput } from '../api/apiKeys'

import { computed, shallowRef } from 'vue'
import { useRouter } from 'vue-router'

import { useApiKeys } from '../model/useApiKeys'

import ApiKeyCreatedDialog from './ApiKeyCreatedDialog.vue'
import CreateApiKeyDialog from './CreateApiKeyDialog.vue'
import RevokeApiKeyDialog from './RevokeApiKeyDialog.vue'

const router = useRouter()
const {
  state,
  apiKeys,
  feedback,
  createdApiKey,
  isRefreshing,
  isCreating,
  revokingKeyId,
  refresh,
  create,
  revoke,
  clearFeedback,
  clearCreatedApiKey,
} = useApiKeys({
  onSessionExpired: () => router.replace({
    path: '/login',
    query: { redirect: '/api-keys' },
  }),
})

const createDialogOpen = shallowRef(false)
const selectedApiKey = shallowRef<ApiKeyMetadata | null>(null)
const createdDialogOpen = computed({
  get: () => createdApiKey.value !== null,
  set: (open: boolean) => {
    if (!open)
      clearCreatedApiKey()
  },
})
const revokeDialogOpen = computed({
  get: () => selectedApiKey.value !== null,
  set: (open: boolean) => {
    if (!open && revokingKeyId.value === null)
      selectedApiKey.value = null
  },
})

const stateContent = computed(() => {
  if (state.value === 'empty') {
    return {
      icon: 'i-lucide-key-round',
      title: '尚未创建 API Key',
      description: '创建一把 Key 以供脚本或第三方集成使用。',
    }
  }
  if (state.value === 'forbidden') {
    return {
      icon: 'i-lucide-shield-alert',
      title: '无权管理 API Key',
      description: '当前身份没有访问 API Key 的权限。',
    }
  }
  return {
    icon: 'i-lucide-wifi-off',
    title: '无法加载 API Key',
    description: '尚未获得可显示的 API Key 列表，请稍后重试。',
  }
})

function openCreateDialog() {
  clearFeedback()
  createDialogOpen.value = true
}

async function submitCreate(input: CreateApiKeyInput) {
  const created = await create(input)
  if (created)
    createDialogOpen.value = false
}

function openRevokeDialog(apiKey: ApiKeyMetadata) {
  clearFeedback()
  selectedApiKey.value = apiKey
}

async function confirmRevoke() {
  const selected = selectedApiKey.value
  if (selected === null)
    return

  const revoked = await revoke(selected)
  if (revoked)
    selectedApiKey.value = null
}
</script>

<template>
  <UDashboardPanel id="api-keys">
    <template #header>
      <UDashboardNavbar title="API Keys">
        <template #leading>
          <UDashboardSidebarCollapse />
        </template>

        <template #right>
          <UTooltip text="刷新 API Key 列表">
            <UButton
              aria-label="刷新 API Key 列表"
              class="size-8"
              color="neutral"
              icon="i-lucide-refresh-cw"
              square
              variant="ghost"
              :disabled="isRefreshing"
              :ui="{ leadingIcon: isRefreshing ? 'animate-spin' : '' }"
              @click="refresh"
            />
          </UTooltip>
          <UButton
            data-testid="create-api-key"
            label="创建 API Key"
            icon="i-lucide-key-round"
            :disabled="state === 'forbidden'"
            @click="openCreateDialog"
          />
        </template>
      </UDashboardNavbar>
    </template>

    <template #body>
      <div
        v-if="state === 'loading'"
        aria-label="正在加载 API Key"
        class="space-y-3"
        data-testid="api-keys-loading"
      >
        <USkeleton v-for="row in 4" :key="row" class="h-16 w-full" />
      </div>

      <section
        v-else-if="state === 'empty' || state === 'failed' || state === 'forbidden'"
        :data-testid="state === 'empty' ? 'api-keys-empty' : `api-keys-${state}`"
        class="mx-auto flex min-h-72 max-w-md flex-col items-center justify-center py-12 text-center"
      >
        <span class="mb-4 flex size-11 items-center justify-center rounded-md bg-elevated text-muted">
          <UIcon :name="stateContent.icon" class="size-5" />
        </span>
        <h2 class="text-base font-semibold text-highlighted">
          {{ stateContent.title }}
        </h2>
        <p class="mt-2 text-sm text-muted">
          {{ stateContent.description }}
        </p>
        <UButton
          v-if="state === 'empty'"
          class="mt-6"
          label="创建 API Key"
          icon="i-lucide-key-round"
          @click="openCreateDialog"
        />
        <UButton
          v-else-if="state === 'failed'"
          class="mt-6"
          color="neutral"
          icon="i-lucide-refresh-cw"
          label="重新加载"
          variant="outline"
          @click="refresh"
        />
        <UButton
          v-else
          class="mt-6"
          color="neutral"
          icon="i-lucide-arrow-left"
          label="返回概览"
          to="/"
          variant="outline"
        />
      </section>

      <div v-else class="space-y-3">
        <article
          v-for="apiKey in apiKeys"
          :key="apiKey.id"
          class="grid gap-3 border-b border-default py-4 sm:grid-cols-[minmax(0,1fr)_auto] sm:items-center"
        >
          <div class="min-w-0">
            <div class="flex min-w-0 flex-wrap items-center gap-2">
              <h2 class="truncate font-medium text-highlighted">
                {{ apiKey.name }}
              </h2>
              <UBadge
                :color="apiKey.status === 'active' ? 'success' : apiKey.status === 'expired' ? 'warning' : 'error'"
                variant="subtle"
              >
                {{ apiKey.status === 'active' ? '有效' : apiKey.status === 'expired' ? '已过期' : '已撤销' }}
              </UBadge>
            </div>
            <code class="mt-1 block overflow-wrap-anywhere text-xs text-muted">
              {{ apiKey.displayPrefix }}
            </code>
            <p class="mt-2 text-xs text-muted">
              创建于 {{ new Intl.DateTimeFormat('zh-CN', { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(apiKey.createdAtUtc)) }}
              <template v-if="apiKey.lastUsedAtUtc">
                · 最近使用 {{ new Intl.DateTimeFormat('zh-CN', { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(apiKey.lastUsedAtUtc)) }}
              </template>
              <template v-if="apiKey.expiresAtUtc">
                · 到期 {{ new Intl.DateTimeFormat('zh-CN', { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(apiKey.expiresAtUtc)) }}
              </template>
            </p>
          </div>

          <UButton
            v-if="apiKey.status !== 'revoked'"
            :data-testid="`revoke-${apiKey.id}`"
            aria-label="撤销 API Key"
            color="error"
            icon="i-lucide-trash-2"
            square
            variant="ghost"
            @click="openRevokeDialog(apiKey)"
          />
        </article>

        <p
          v-if="feedback"
          role="status"
          aria-live="polite"
          class="text-sm text-error"
        >
          {{ feedback.message }}
        </p>
      </div>
    </template>
  </UDashboardPanel>

  <CreateApiKeyDialog
    v-model:open="createDialogOpen"
    :is-creating="isCreating"
    :feedback="feedback"
    @create="submitCreate"
  />
  <ApiKeyCreatedDialog
    v-model:open="createdDialogOpen"
    :created-api-key="createdApiKey"
  />
  <RevokeApiKeyDialog
    v-model:open="revokeDialogOpen"
    :api-key="selectedApiKey"
    :is-submitting="revokingKeyId !== null"
    :feedback="feedback"
    @confirm="confirmRevoke"
  />
</template>

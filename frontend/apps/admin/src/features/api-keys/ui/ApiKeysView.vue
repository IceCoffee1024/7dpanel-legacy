<script setup lang="ts">
import type { ApiKeyMetadata, CreateApiKeyInput } from '../api/apiKeys'

import { computed, shallowRef } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'

import { useApiKeys } from '../model/useApiKeys'

import ApiKeyCreatedDialog from './ApiKeyCreatedDialog.vue'
import CreateApiKeyDialog from './CreateApiKeyDialog.vue'
import RevokeApiKeyDialog from './RevokeApiKeyDialog.vue'

const router = useRouter()
const { d, t } = useI18n()
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
    query: { redirect: '/system/api-keys' },
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
      title: t('apiKeys.state.emptyTitle'),
      description: t('apiKeys.state.emptyDescription'),
    }
  }
  if (state.value === 'forbidden') {
    return {
      icon: 'i-lucide-shield-alert',
      title: t('apiKeys.state.forbiddenTitle'),
      description: t('apiKeys.state.forbiddenDescription'),
    }
  }
  return {
    icon: 'i-lucide-wifi-off',
    title: t('apiKeys.state.failedTitle'),
    description: t('apiKeys.state.failedDescription'),
  }
})
const feedbackMessage = computed(() => feedback.value === null
  ? ''
  : t(`apiKeys.feedback.${feedback.value.code}`))

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
      <UDashboardNavbar :title="t('apiKeys.title')">
        <template #leading>
          <UDashboardSidebarCollapse />
        </template>

        <template #right>
          <UTooltip :text="t('apiKeys.refresh')">
            <UButton
              :aria-label="t('apiKeys.refresh')"
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
            :label="t('apiKeys.create')"
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
        :aria-label="t('apiKeys.loading')"
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
          :label="t('apiKeys.create')"
          icon="i-lucide-key-round"
          @click="openCreateDialog"
        />
        <UButton
          v-else-if="state === 'failed'"
          class="mt-6"
          color="neutral"
          icon="i-lucide-refresh-cw"
          :label="t('common.reload')"
          variant="outline"
          @click="refresh"
        />
        <UButton
          v-else
          class="mt-6"
          color="neutral"
          icon="i-lucide-arrow-left"
          :label="t('common.backToOverview')"
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
                {{ t(`apiKeys.status.${apiKey.status}`) }}
              </UBadge>
            </div>
            <code class="mt-1 block overflow-wrap-anywhere text-xs text-muted">
              {{ apiKey.displayPrefix }}
            </code>
            <p class="mt-2 text-xs text-muted">
              {{ t('apiKeys.dates.created', { time: d(new Date(apiKey.createdAtUtc), 'medium') }) }}
              <template v-if="apiKey.lastUsedAtUtc">
                · {{ t('apiKeys.dates.lastUsed', { time: d(new Date(apiKey.lastUsedAtUtc), 'medium') }) }}
              </template>
              <template v-if="apiKey.expiresAtUtc">
                · {{ t('apiKeys.dates.expires', { time: d(new Date(apiKey.expiresAtUtc), 'medium') }) }}
              </template>
            </p>
          </div>

          <UButton
            v-if="apiKey.status !== 'revoked'"
            :data-testid="`revoke-${apiKey.id}`"
            :aria-label="t('apiKeys.revokeDialog.ariaLabel')"
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
          {{ feedbackMessage }}
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

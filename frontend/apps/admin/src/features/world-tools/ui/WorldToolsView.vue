<script setup lang="ts">
import type { WorldOperationReview } from '../model/worldOperationForm'

import { computed, onMounted, shallowRef } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'

import { useAuthStore } from '../../auth'
import { useUndoPreflight } from '../model/useUndoPreflight'
import { useWorldOperations } from '../model/useWorldOperations'
import { useWorldResources } from '../model/useWorldResources'
import WorldOperationConfirmDialog from './WorldOperationConfirmDialog.vue'
import WorldOperationHistory from './WorldOperationHistory.vue'
import WorldOperationPanel from './WorldOperationPanel.vue'
import WorldReadDetails from './WorldReadDetails.vue'

const auth = useAuthStore()
const { t } = useI18n()
const route = useRoute()
const router = useRouter()
const activeTab = shallowRef<'resources' | 'operations'>('resources')
const pendingReview = shallowRef<WorldOperationReview | null>(null)
const submissionFeedback = shallowRef<string | null>(null)
const tabs = computed(() => [
  { label: t('worldTools.tabs.resources'), value: 'resources', slot: 'resources' as const, icon: 'i-lucide-map' },
  { label: t('worldTools.tabs.operations'), value: 'operations', slot: 'operations' as const, icon: 'i-lucide-wrench' },
])

function handleSessionExpired() {
  auth.expireSession()
  void router.replace({
    path: '/login',
    query: { redirect: route.fullPath },
  })
}

function replaceOperationId(operationId: string | null) {
  const query = { ...route.query }
  if (operationId === null)
    delete query.operationId
  else
    query.operationId = operationId
  void router.replace({ query })
}

const resources = useWorldResources({ onSessionExpired: handleSessionExpired })
const operations = useWorldOperations({
  onSessionExpired: handleSessionExpired,
  replaceOperationId,
})
const undoPreflight = useUndoPreflight({ onSessionExpired: handleSessionExpired })

function operationIdFromQuery(value: unknown): string | null {
  if (typeof value === 'string' && value.trim() !== '')
    return value
  if (Array.isArray(value)) {
    const first = value.find(item => typeof item === 'string' && item.trim() !== '')
    return typeof first === 'string' ? first : null
  }
  return null
}

function reviewOperation(review: WorldOperationReview) {
  submissionFeedback.value = null
  pendingReview.value = review
}

function cancelReview() {
  if (operations.state.value !== 'submitting')
    pendingReview.value = null
}

async function confirmOperation() {
  const review = pendingReview.value
  if (review === null)
    return

  submissionFeedback.value = null
  await resources.refresh()
  const latest = resources.summary.value.data
  if (resources.summary.value.phase !== 'ready'
    || latest === null
    || (latest.sourceState !== 'Success' && latest.sourceState !== 'Partial')
    || latest.worldId !== review.worldId
    || latest.worldVersion !== review.worldVersion
    || latest.mapResourceVersion !== review.mapResourceVersion) {
    pendingReview.value = null
    submissionFeedback.value = t('worldTools.view.worldChanged')
    return
  }

  await operations.submit(review.submission)
  if (operations.receipt.value !== null) {
    pendingReview.value = null
    activeTab.value = 'operations'
  }
}

onMounted(() => {
  const operationId = operationIdFromQuery(route.query.operationId)
  if (operationId !== null) {
    activeTab.value = 'operations'
    void operations.resume(operationId)
  }
})
</script>

<template>
  <UDashboardPanel id="world-tools">
    <template #header>
      <UDashboardNavbar icon="i-lucide-map-cog" :title="t('worldTools.title')">
        <template #leading>
          <UDashboardSidebarCollapse />
        </template>
        <template #right>
          <UButton
            color="neutral"
            icon="i-lucide-refresh-cw"
            :label="t('worldTools.common.refresh')"
            variant="outline"
            @click="resources.refresh"
          />
        </template>
      </UDashboardNavbar>
    </template>

    <template #body>
      <div class="mx-auto w-full max-w-7xl space-y-4 sm:space-y-6">
        <UAlert
          v-if="submissionFeedback"
          color="warning"
          icon="i-lucide-triangle-alert"
          :title="t('worldTools.view.notSubmitted')"
          :description="submissionFeedback"
          variant="subtle"
        />

        <UTabs v-model="activeTab" :items="tabs" class="w-full">
          <template #resources>
            <div class="pt-4">
              <WorldReadDetails
                :summary="resources.summary.value"
                :land-claims="resources.landClaims.value"
                :vehicles="resources.vehicles.value"
                :drones="resources.drones.value"
                :containers="resources.containers.value"
                :block-catalog="resources.blockCatalog.value"
                :prefab-catalog="resources.prefabCatalog.value"
                :entity-type-catalog="resources.entityTypeCatalog.value"
              />
            </div>
          </template>

          <template #operations>
            <div class="space-y-6 pt-4">
              <WorldOperationPanel
                :summary="resources.summary.value.data"
                :can-mutate="operations.canMutate.value"
                :submitting="operations.state.value === 'submitting'"
                :undo-preflight-phase="undoPreflight.phase.value"
                :undo-preflight="undoPreflight.data.value"
                :undo-preflight-error-code="undoPreflight.errorCode.value"
                :block-catalog="resources.blockCatalog.value.data"
                :prefab-catalog="resources.prefabCatalog.value.data"
                :entity-type-catalog="resources.entityTypeCatalog.value.data"
                @review="reviewOperation"
                @request-undo-preflight="undoPreflight.load"
                @clear-undo-preflight="undoPreflight.clear"
              />
              <WorldOperationHistory
                :operation="operations.operation.value"
                :receipt="operations.receipt.value"
                :state="operations.state.value"
                :error-code="operations.errorCode.value"
                @clear="operations.clear"
                @refresh="operations.resume"
              />
            </div>
          </template>
        </UTabs>

        <WorldOperationConfirmDialog
          :open="pendingReview !== null"
          :review="pendingReview"
          :submitting="operations.state.value === 'submitting'"
          @cancel="cancelReview"
          @confirm="confirmOperation"
        />
      </div>
    </template>
  </UDashboardPanel>
</template>

<script setup lang="ts">
import type { WorldCatalog, WorldSummary } from '../api/worldTools'
import type { WorldOperationReview } from '../model/worldOperationForm'

import { computed, reactive, shallowRef } from 'vue'
import { useI18n } from 'vue-i18n'
import {
  createInitialWorldOperationForm,
  createWorldOperationReview,
  WorldOperationFormError,
} from '../model/worldOperationForm'

const props = withDefaults(defineProps<{
  summary: WorldSummary | null
  canMutate: boolean
  submitting: boolean
  blockCatalog?: WorldCatalog | null
  prefabCatalog?: WorldCatalog | null
  entityTypeCatalog?: WorldCatalog | null
}>(), {
  blockCatalog: null,
  prefabCatalog: null,
  entityTypeCatalog: null,
})
const emit = defineEmits<{ review: [review: WorldOperationReview] }>()
const { t } = useI18n()
const form = reactive(createInitialWorldOperationForm())
const feedback = shallowRef<string | null>(null)

const operationItems = computed(() => [
  { label: t('worldTools.operations.types.deleteLandClaim'), value: 'deleteLandClaim' },
  { label: t('worldTools.operations.types.moveOnlinePlayer'), value: 'moveOnlinePlayer' },
  { label: t('worldTools.operations.types.moveEntity'), value: 'moveEntity' },
  { label: t('worldTools.operations.types.copyRegion'), value: 'copyRegion' },
  { label: t('worldTools.operations.types.fillRegion'), value: 'fillRegion' },
  { label: t('worldTools.operations.types.clearRegion'), value: 'clearRegion' },
  { label: t('worldTools.operations.types.pasteRegion'), value: 'pasteRegion' },
  { label: t('worldTools.operations.types.setBlock'), value: 'setBlock' },
  { label: t('worldTools.operations.types.placePrefab'), value: 'placePrefab' },
  { label: t('worldTools.operations.types.removePrefab'), value: 'removePrefab' },
  { label: t('worldTools.operations.types.spawnEntity'), value: 'spawnEntity' },
  { label: t('worldTools.operations.types.deleteEntity'), value: 'deleteEntity' },
  { label: t('worldTools.operations.types.cleanupEntities'), value: 'cleanupEntities' },
  { label: t('worldTools.operations.types.reloadResource'), value: 'reloadResource' },
  { label: t('worldTools.operations.types.collectGarbage'), value: 'collectGarbage' },
  { label: t('worldTools.operations.types.undoChangeSet'), value: 'undoChangeSet' },
  { label: t('worldTools.operations.types.refreshMapResources'), value: 'refreshMapResources' },
  { label: t('worldTools.operations.types.renderExploredMap'), value: 'renderExploredMap' },
  { label: t('worldTools.operations.types.renderFullMap'), value: 'renderFullMap' },
])
const blockShapeItems = ['Default', 'Cube', 'Ramp', 'Wedge']
const entityCategoryItems = ['Animal', 'Hostile', 'Vehicle', 'Drone', 'DroppedItem']
const reloadResourceItems = ['Blocks', 'Items', 'EntityClasses', 'Prefabs']

const needsTarget = computed(() => ['deleteLandClaim', 'moveOnlinePlayer', 'moveEntity', 'deleteEntity'].includes(form.type))
const needsEntityId = computed(() => ['moveOnlinePlayer', 'moveEntity', 'deleteEntity'].includes(form.type))
const needsOwnerIdentity = computed(() => ['deleteLandClaim', 'moveEntity', 'deleteEntity'].includes(form.type))
const needsEntityType = computed(() => ['moveEntity', 'spawnEntity', 'deleteEntity'].includes(form.type))
const needsObservedPosition = computed(() => ['deleteLandClaim', 'moveEntity', 'setBlock', 'placePrefab', 'removePrefab', 'spawnEntity', 'deleteEntity', 'cleanupEntities'].includes(form.type))
const needsDestination = computed(() => ['moveOnlinePlayer', 'moveEntity'].includes(form.type))
const needsRegion = computed(() => ['copyRegion', 'fillRegion', 'clearRegion', 'pasteRegion', 'placePrefab', 'removePrefab'].includes(form.type))
const needsCatalog = computed(() => ['fillRegion', 'setBlock', 'placePrefab', 'removePrefab', 'spawnEntity', 'deleteEntity'].includes(form.type))
const needsMapBounds = computed(() => ['refreshMapResources', 'renderExploredMap', 'renderFullMap'].includes(form.type))
const activeCatalog = computed(() => {
  if (form.type === 'fillRegion' || form.type === 'setBlock')
    return props.blockCatalog
  if (form.type === 'placePrefab' || form.type === 'removePrefab')
    return props.prefabCatalog
  if (form.type === 'spawnEntity' || form.type === 'deleteEntity')
    return props.entityTypeCatalog
  return null
})
const activeCatalogItems = computed(() => [...(activeCatalog.value?.items ?? [])])
const blockCatalogItems = computed(() => [...(props.blockCatalog?.items ?? [])])
const prefabCatalogItems = computed(() => [...(props.prefabCatalog?.items ?? [])])
const snapshotReady = computed(() => props.summary !== null
  && (props.summary.sourceState === 'Success' || props.summary.sourceState === 'Partial')
  && props.summary.worldId !== null
  && props.summary.worldVersion !== null)

function prepareReview() {
  feedback.value = null
  if (props.summary === null) {
    feedback.value = t('worldTools.operations.feedback.snapshotRequired')
    return
  }
  if (needsCatalog.value) {
    if (activeCatalog.value?.catalogVersion === null || activeCatalog.value?.catalogVersion === undefined) {
      feedback.value = t('worldTools.operations.feedback.catalogUnavailable')
      return
    }
    form.catalogVersion = activeCatalog.value.catalogVersion
  }
  try {
    emit('review', createWorldOperationReview(form, props.summary))
  }
  catch (cause) {
    feedback.value = cause instanceof WorldOperationFormError ? cause.message : t('worldTools.operations.feedback.invalidForm')
  }
}
</script>

<template>
  <section class="space-y-4" aria-labelledby="world-operation-panel-title">
    <div>
      <h2 id="world-operation-panel-title" class="font-semibold text-highlighted">
        {{ t('worldTools.operations.title') }}
      </h2>
      <p class="text-sm text-muted">
        {{ t('worldTools.operations.description') }}
      </p>
    </div>

    <UAlert
      v-if="!props.canMutate"
      color="neutral"
      icon="i-lucide-lock-keyhole"
      :title="t('worldTools.operations.ownerRequiredTitle')"
      :description="t('worldTools.operations.ownerRequiredDescription')"
      variant="subtle"
    />
    <UAlert
      v-else-if="!snapshotReady"
      color="warning"
      icon="i-lucide-clock-alert"
      :title="t('worldTools.operations.snapshotRequiredTitle')"
      :description="t('worldTools.operations.snapshotRequiredDescription')"
      variant="subtle"
    />

    <form
      v-if="props.canMutate"
      data-testid="world-operation-form"
      class="space-y-4 rounded-lg border border-default p-4"
      @submit.prevent="prepareReview"
    >
      <UFormField :label="t('worldTools.operations.fields.operation')" required>
        <USelect v-model="form.type" class="w-full" :items="operationItems" />
      </UFormField>

      <div v-if="needsTarget" class="grid gap-3 sm:grid-cols-2">
        <UFormField :label="form.type === 'moveOnlinePlayer' ? t('worldTools.operations.fields.crossplatformId') : t('worldTools.operations.fields.targetId')" required>
          <UInput v-model="form.targetId" class="w-full" />
        </UFormField>
        <UFormField v-if="needsEntityId" :label="t('worldTools.operations.fields.entityId')" required>
          <UInputNumber v-model="form.entityId" class="w-full" :min="0" />
        </UFormField>
      </div>
      <UFormField v-if="needsOwnerIdentity" :label="t('worldTools.operations.fields.ownerStableIdentity')" :required="form.type === 'deleteLandClaim'">
        <UInput v-model="form.ownerStableIdentity" class="w-full" />
      </UFormField>
      <UFormField v-if="form.type === 'moveOnlinePlayer'" :label="t('worldTools.operations.fields.onlineObservedAtUtc')" required>
        <UInput v-model="form.onlineObservedAtUtc" class="w-full" placeholder="2026-07-26T10:00:00Z" />
      </UFormField>

      <UFormField v-if="needsEntityType" :label="t('worldTools.operations.fields.entityTypeResourceId')" required>
        <USelect
          v-if="activeCatalog"
          v-model="form.entityTypeResourceId"
          class="w-full"
          :items="activeCatalogItems"
        />
        <UInput v-else v-model="form.entityTypeResourceId" class="w-full" />
      </UFormField>

      <fieldset v-if="needsObservedPosition" class="space-y-2">
        <legend class="text-sm font-medium text-highlighted">
          {{ ['spawnEntity', 'cleanupEntities'].includes(form.type) ? t('worldTools.operations.fields.center') : form.type === 'setBlock' ? t('worldTools.operations.fields.coordinate') : form.type === 'placePrefab' || form.type === 'removePrefab' ? t('worldTools.operations.fields.anchor') : t('worldTools.operations.fields.observedPosition') }}
        </legend>
        <div class="grid gap-3 sm:grid-cols-3">
          <UFormField label="X">
            <UInputNumber v-model="form.observedX" class="w-full" />
          </UFormField>
          <UFormField label="Y">
            <UInputNumber v-model="form.observedY" class="w-full" />
          </UFormField>
          <UFormField label="Z">
            <UInputNumber v-model="form.observedZ" class="w-full" />
          </UFormField>
        </div>
      </fieldset>

      <fieldset v-if="needsDestination" class="space-y-2">
        <legend class="text-sm font-medium text-highlighted">
          {{ t('worldTools.operations.fields.destination') }}
        </legend>
        <div class="grid gap-3 sm:grid-cols-3">
          <UFormField label="X">
            <UInputNumber v-model="form.destinationX" class="w-full" />
          </UFormField>
          <UFormField label="Y">
            <UInputNumber v-model="form.destinationY" class="w-full" />
          </UFormField>
          <UFormField label="Z">
            <UInputNumber v-model="form.destinationZ" class="w-full" />
          </UFormField>
        </div>
      </fieldset>

      <fieldset v-if="needsRegion" class="space-y-3">
        <legend class="text-sm font-medium text-highlighted">
          {{ t('worldTools.operations.fields.boundedRegion') }}
        </legend>
        <div class="grid gap-3 sm:grid-cols-3">
          <UFormField :label="t('worldTools.operations.fields.firstX')">
            <UInputNumber v-model="form.firstX" class="w-full" />
          </UFormField>
          <UFormField :label="t('worldTools.operations.fields.firstY')">
            <UInputNumber v-model="form.firstY" class="w-full" />
          </UFormField>
          <UFormField :label="t('worldTools.operations.fields.firstZ')">
            <UInputNumber v-model="form.firstZ" class="w-full" />
          </UFormField>
          <UFormField :label="t('worldTools.operations.fields.secondX')">
            <UInputNumber v-model="form.secondX" class="w-full" />
          </UFormField>
          <UFormField :label="t('worldTools.operations.fields.secondY')">
            <UInputNumber v-model="form.secondY" class="w-full" />
          </UFormField>
          <UFormField :label="t('worldTools.operations.fields.secondZ')">
            <UInputNumber v-model="form.secondZ" class="w-full" />
          </UFormField>
        </div>
      </fieldset>

      <UAlert
        v-if="needsCatalog && !activeCatalog"
        color="warning"
        :title="t('worldTools.operations.catalogUnavailable')"
        variant="subtle"
      />
      <UFormField v-if="form.type === 'fillRegion' || form.type === 'setBlock'" :label="t('worldTools.operations.fields.blockInternalName')" required>
        <USelect v-model="form.blockInternalName" class="w-full" :items="blockCatalogItems" />
      </UFormField>
      <div v-if="form.type === 'setBlock'" class="grid gap-3 sm:grid-cols-2">
        <UFormField :label="t('worldTools.operations.fields.rotation')" required>
          <UInputNumber v-model="form.rotation" class="w-full" :min="0" />
        </UFormField>
        <UFormField :label="t('worldTools.operations.fields.shape')">
          <USelect v-model="form.blockShape" class="w-full" :items="blockShapeItems" />
        </UFormField>
      </div>

      <template v-if="form.type === 'placePrefab' || form.type === 'removePrefab'">
        <UFormField :label="t('worldTools.operations.fields.prefabResourceId')" required>
          <USelect v-model="form.prefabResourceId" class="w-full" :items="prefabCatalogItems" />
        </UFormField>
        <UFormField v-if="form.type === 'removePrefab'" :label="t('worldTools.operations.fields.prefabInstanceId')" required>
          <UInput v-model="form.prefabInstanceId" class="w-full" />
        </UFormField>
        <UFormField :label="t('worldTools.operations.fields.rotation')" required>
          <UInputNumber v-model="form.rotation" class="w-full" :min="0" />
        </UFormField>
      </template>

      <div v-if="form.type === 'deleteLandClaim' || form.type === 'spawnEntity' || form.type === 'cleanupEntities'" class="grid gap-3 sm:grid-cols-2">
        <UFormField v-if="form.type === 'spawnEntity'" :label="t('worldTools.operations.fields.quantity')" required>
          <UInputNumber v-model="form.quantity" class="w-full" :min="1" />
        </UFormField>
        <UFormField :label="t('worldTools.operations.fields.radius')" required>
          <UInputNumber v-model="form.radius" class="w-full" :min="0" />
        </UFormField>
        <UFormField v-if="form.type === 'cleanupEntities'" :label="t('worldTools.operations.fields.maximumCount')" required>
          <UInputNumber v-model="form.maximumCount" class="w-full" :min="1" />
        </UFormField>
      </div>
      <UFormField v-if="form.type === 'cleanupEntities'" :label="t('worldTools.operations.fields.entityCategory')" required>
        <USelect v-model="form.entityCategory" class="w-full" :items="entityCategoryItems" />
      </UFormField>
      <UFormField v-if="form.type === 'reloadResource'" :label="t('worldTools.operations.fields.resourceCategory')" required>
        <USelect v-model="form.reloadResourceKind" class="w-full" :items="reloadResourceItems" />
      </UFormField>
      <UFormField v-if="form.type === 'pasteRegion'" :label="t('worldTools.operations.fields.sourceChangeSetId')" required>
        <UInput v-model="form.sourceChangeSetId" class="w-full" />
      </UFormField>

      <div v-if="form.type === 'undoChangeSet'" class="grid gap-3">
        <UFormField :label="t('worldTools.operations.fields.sourceOperationId')" required>
          <UInput v-model="form.sourceOperationId" class="w-full" />
        </UFormField>
        <UFormField :label="t('worldTools.operations.fields.changeSetId')" required>
          <UInput v-model="form.changeSetId" class="w-full" />
        </UFormField>
        <UFormField :label="t('worldTools.operations.fields.currentRegionHash')" required>
          <UInput v-model="form.currentRegionHash" class="w-full" />
        </UFormField>
      </div>

      <fieldset v-if="needsMapBounds" class="space-y-3">
        <legend class="text-sm font-medium text-highlighted">
          {{ t('worldTools.operations.fields.mapScope') }}
        </legend>
        <UCheckbox v-model="form.boundsEnabled" :label="t('worldTools.operations.fields.limitBounds')" />
        <div v-if="form.boundsEnabled" class="grid gap-3 sm:grid-cols-2">
          <UFormField :label="t('worldTools.operations.fields.minimumX')">
            <UInputNumber v-model="form.minimumX" class="w-full" />
          </UFormField>
          <UFormField :label="t('worldTools.operations.fields.minimumZ')">
            <UInputNumber v-model="form.minimumZ" class="w-full" />
          </UFormField>
          <UFormField :label="t('worldTools.operations.fields.maximumX')">
            <UInputNumber v-model="form.maximumX" class="w-full" />
          </UFormField>
          <UFormField :label="t('worldTools.operations.fields.maximumZ')">
            <UInputNumber v-model="form.maximumZ" class="w-full" />
          </UFormField>
        </div>
      </fieldset>

      <UAlert
        v-if="feedback"
        color="error"
        :description="feedback"
        :title="t('worldTools.operations.cannotReview')"
        variant="subtle"
      />
      <div class="flex justify-end">
        <UButton
          type="submit"
          color="warning"
          icon="i-lucide-shield-alert"
          :label="t('worldTools.operations.review')"
          :disabled="!snapshotReady || props.submitting"
          :loading="props.submitting"
        />
      </div>
    </form>
  </section>
</template>

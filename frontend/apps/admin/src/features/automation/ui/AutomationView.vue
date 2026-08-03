<script setup lang="ts">
import type { AutomationAction, AutomationActionType, AutomationConditionOperator, AutomationRuleDraft, AutomationTargetKind, AutomationTriggerSnapshot, AutomationTriggerType } from '../api/automation'
import type { AutomationController } from '../model/useAutomation'

import { computed, reactive, shallowRef, watch } from 'vue'
import { useI18n } from 'vue-i18n'

import { operationStatus } from '../../../shared/model/operationStatus'

const props = defineProps<{ controller: AutomationController }>()
const { t } = useI18n()
const deleteConfirmationOpen = shallowRef(false)
const form = reactive({ id: '', name: '', isEnabled: true, trigger: 'PlayerJoined' as AutomationTriggerType, fieldKey: 'actor.group', operator: 'Equals' as AutomationConditionOperator, scalarValue: '', actionType: 'PrivateMessage' as AutomationActionType, targetKind: 'TriggerPlayer' as AutomationTargetKind, targetReference: '', actionValue: '', amount: 1, durationSeconds: 60, cooldownSeconds: 0, cooldownScope: 'RulePlayer' as const, concurrencyPolicy: 'SkipIfRunning' as const, failurePolicy: 'StopOnFailure' as const })
const snapshot = reactive({ triggerId: '', occurredAtUtc: new Date().toISOString(), crossplatformId: '', entityId: '', group: '', permissionLevel: '', chatText: '', scheduledForUtc: '', phase: '', gapIds: '' })
const triggerItems = computed(() => [
  { label: t('automation.enums.trigger.PlayerJoined'), value: 'PlayerJoined' },
  { label: t('automation.enums.trigger.PlayerLeft'), value: 'PlayerLeft' },
  { label: t('automation.enums.trigger.ChatMessage'), value: 'ChatMessage' },
  { label: t('automation.enums.trigger.Cron'), value: 'Cron' },
  { label: t('automation.enums.trigger.BloodMoonPhaseEntered'), value: 'BloodMoonPhaseEntered' },
])
const operatorItems = computed(() => [
  { label: t('automation.enums.operator.Equals'), value: 'Equals' },
  { label: t('automation.enums.operator.NotEquals'), value: 'NotEquals' },
  { label: t('automation.enums.operator.InSet'), value: 'InSet' },
  { label: t('automation.enums.operator.NumberRange'), value: 'NumberRange' },
  { label: t('automation.enums.operator.TimeWindow'), value: 'TimeWindow' },
  { label: t('automation.enums.operator.PlayerGroup'), value: 'PlayerGroup' },
  { label: t('automation.enums.operator.Permission'), value: 'Permission' },
  { label: t('automation.enums.operator.Cooldown'), value: 'Cooldown' },
])
const actionItems = computed(() => [
  { label: t('automation.enums.action.BroadcastMessage'), value: 'BroadcastMessage' },
  { label: t('automation.enums.action.PrivateMessage'), value: 'PrivateMessage' },
  { label: t('automation.enums.action.Announcement'), value: 'Announcement' },
  { label: t('automation.enums.action.GrantItem'), value: 'GrantItem' },
  { label: t('automation.enums.action.GrantRewardPackage'), value: 'GrantRewardPackage' },
  { label: t('automation.enums.action.AdjustEconomy'), value: 'AdjustEconomy' },
  { label: t('automation.enums.action.KickPlayer'), value: 'KickPlayer' },
  { label: t('automation.enums.action.MutePlayer'), value: 'MutePlayer' },
  { label: t('automation.enums.action.RestrictedCommand'), value: 'RestrictedCommand' },
  { label: t('automation.enums.action.DiscordMessage'), value: 'DiscordMessage' },
])
const targetItems = computed(() => [
  { label: t('automation.enums.target.Global'), value: 'Global' },
  { label: t('automation.enums.target.TriggerPlayer'), value: 'TriggerPlayer' },
  { label: t('automation.enums.target.StablePlayer'), value: 'StablePlayer' },
  { label: t('automation.enums.target.DiscordTarget'), value: 'DiscordTarget' },
])
const structureEditable = computed(() => props.controller.selected.value?.condition.kind === 'Predicate' || props.controller.selected.value === null)

watch(() => props.controller.selected.value, (rule) => {
  const predicate = rule?.condition.predicate
  Object.assign(form, rule === null
    ? { id: '', name: '', isEnabled: true, trigger: 'PlayerJoined', fieldKey: 'actor.group', operator: 'Equals', scalarValue: '', actionType: 'PrivateMessage', targetKind: 'TriggerPlayer', targetReference: '', actionValue: '', amount: 1, durationSeconds: 60, cooldownSeconds: 0, cooldownScope: 'RulePlayer', concurrencyPolicy: 'SkipIfRunning', failurePolicy: 'StopOnFailure' }
    : {
        id: rule.id,
        name: rule.name,
        isEnabled: rule.isEnabled,
        trigger: rule.trigger.type,
        fieldKey: predicate?.fieldKey ?? '',
        operator: predicate?.operator ?? 'Equals',
        scalarValue: predicate?.scalarValue ?? '',
        actionType: rule.actions[0]?.type ?? 'PrivateMessage',
        targetKind: rule.actions[0]?.target.kind ?? 'TriggerPlayer',
        targetReference: rule.actions[0]?.target.referenceId ?? '',
        actionValue: actionText(rule.actions[0]),
        amount: actionAmount(rule.actions[0]),
        durationSeconds: rule.actions[0]?.mutePlayer?.durationSeconds ?? 60,
        cooldownSeconds: rule.cooldownSeconds,
        cooldownScope: rule.cooldownScope,
        concurrencyPolicy: rule.concurrencyPolicy,
        failurePolicy: rule.failurePolicy,
      })
}, { immediate: true })

function actionText(action?: AutomationAction) {
  return action?.broadcastMessage?.message ?? action?.privateMessage?.message ?? action?.announcement?.message ?? action?.discordMessage?.message ?? action?.grantItem?.resourceId ?? action?.grantRewardPackage?.rewardPackageId ?? action?.kickPlayer?.reason ?? action?.mutePlayer?.reason ?? action?.restrictedCommand?.commandCatalogKey ?? ''
}
function actionAmount(action?: AutomationAction) {
  return action?.grantItem?.amount ?? action?.adjustEconomy?.amount ?? 1
}
function target() {
  return { kind: form.targetKind, ...(['StablePlayer', 'DiscordTarget'].includes(form.targetKind) ? { referenceId: form.targetReference.trim() } : {}) } as const
}
function action(): AutomationAction {
  const base = { id: props.controller.selected.value?.actions[0]?.id ?? 'action-1', type: form.actionType, target: target() }
  switch (form.actionType) {
    case 'BroadcastMessage': return { ...base, broadcastMessage: { message: form.actionValue.trim() } }
    case 'PrivateMessage': return { ...base, privateMessage: { message: form.actionValue.trim() } }
    case 'Announcement': return { ...base, announcement: { message: form.actionValue.trim() } }
    case 'GrantItem': return { ...base, grantItem: { resourceId: form.actionValue.trim(), amount: form.amount } }
    case 'GrantRewardPackage': return { ...base, grantRewardPackage: { rewardPackageId: form.actionValue.trim() } }
    case 'AdjustEconomy': return { ...base, adjustEconomy: { amount: form.amount } }
    case 'KickPlayer': return { ...base, kickPlayer: { reason: form.actionValue.trim() } }
    case 'MutePlayer': return { ...base, mutePlayer: { durationSeconds: form.durationSeconds, reason: form.actionValue.trim() } }
    case 'RestrictedCommand': return { ...base, restrictedCommand: { commandCatalogKey: form.actionValue.trim() } }
    case 'DiscordMessage': return { ...base, discordMessage: { message: form.actionValue.trim() } }
  }
}
function draft(): AutomationRuleDraft {
  const selected = props.controller.selected.value
  return { id: form.id.trim(), ...(selected === null ? {} : { expectedVersion: selected.version }), name: form.name.trim(), isEnabled: form.isEnabled, trigger: { type: form.trigger }, condition: { nodeId: selected?.condition.nodeId ?? 'root', kind: 'Predicate', predicate: { fieldKey: form.fieldKey.trim(), operator: form.operator, scalarValue: form.scalarValue.trim() } }, actions: [action()], cooldownSeconds: form.cooldownSeconds, cooldownScope: form.cooldownScope, concurrencyPolicy: form.concurrencyPolicy, failurePolicy: form.failurePolicy }
}
function triggerSnapshot(): AutomationTriggerSnapshot {
  return { triggerId: snapshot.triggerId.trim(), trigger: { type: form.trigger }, occurredAtUtc: snapshot.occurredAtUtc, ...(snapshot.crossplatformId || snapshot.entityId || snapshot.group || snapshot.permissionLevel ? { actor: { ...(snapshot.crossplatformId ? { crossplatformId: snapshot.crossplatformId.trim() } : {}), ...(snapshot.entityId ? { entityId: Number(snapshot.entityId) } : {}), ...(snapshot.group ? { group: snapshot.group.trim() } : {}), ...(snapshot.permissionLevel ? { permissionLevel: Number(snapshot.permissionLevel) } : {}) } } : {}), ...(form.trigger === 'ChatMessage' ? { chat: { text: snapshot.chatText } } : {}), ...(form.trigger === 'Cron' ? { cron: { scheduledForUtc: snapshot.scheduledForUtc } } : {}), ...(form.trigger === 'BloodMoonPhaseEntered' ? { bloodMoon: { phase: snapshot.phase } } : {}), gapIds: snapshot.gapIds.split(',').map(value => value.trim()).filter(Boolean) }
}
function requestRemoveSelected() {
  if (props.controller.selected.value)
    deleteConfirmationOpen.value = true
}
function removeSelected() {
  const rule = props.controller.selected.value
  if (!rule)
    return
  deleteConfirmationOpen.value = false
  void props.controller.remove(rule)
}
</script>

<template>
  <UDashboardPanel id="automation">
    <template #header>
      <UDashboardNavbar :title="t('automation.title')">
        <template #leading>
          <UDashboardSidebarCollapse />
        </template><template #right>
          <UButton
            color="neutral"
            icon="i-lucide-refresh-cw"
            :label="t('automation.common.refresh')"
            variant="outline"
            :loading="controller.state.value === 'loading'"
            @click="controller.refresh"
          />
        </template>
      </UDashboardNavbar>
    </template>
    <template #body>
      <UContainer class="space-y-5 py-5">
        <USkeleton v-if="controller.state.value === 'loading'" class="h-48 w-full" />
        <UAlert v-else-if="controller.state.value === 'forbidden'" color="error" :title="t('automation.state.forbidden')" />
        <UAlert v-else-if="controller.state.value === 'failed'" color="error" :title="t('automation.state.unavailable')">
          <template #actions>
            <UButton
              color="neutral"
              :label="t('automation.common.retry')"
              variant="outline"
              @click="controller.refresh"
            />
          </template>
        </UAlert>
        <UAlert v-else-if="controller.state.value === 'stale'" color="warning" :title="t('automation.state.stale')" />
        <UAlert
          v-if="controller.errorCode.value"
          color="error"
          :title="t('automation.state.operationIncomplete')"
          :description="controller.errorCode.value"
        />
        <UAlert
          v-if="controller.executionState.value === 'unavailable'"
          color="warning"
          :title="t('automation.execution.unavailableTitle')"
          :description="t('automation.execution.unavailableDescription')"
        />

        <div class="grid gap-5 xl:grid-cols-[18rem_minmax(0,1fr)]">
          <UCard>
            <template #header>
              <div class="flex items-center justify-between">
                <h2 class="font-semibold">
                  {{ t('automation.rules.title') }}
                </h2><UButton
                  icon="i-lucide-plus"
                  :label="t('automation.rules.create')"
                  size="sm"
                  @click="controller.select(null)"
                />
              </div>
            </template><div v-if="controller.rules.value.length === 0" class="py-6 text-center text-sm text-muted">
              {{ t('automation.rules.empty') }}
            </div><div v-else class="space-y-2">
              <UButton
                v-for="rule in controller.rules.value"
                :key="rule.id"
                block
                :color="controller.selected.value?.id === rule.id ? 'primary' : 'neutral'"
                :label="rule.name"
                :variant="controller.selected.value?.id === rule.id ? 'soft' : 'ghost'"
                @click="controller.select(rule)"
              />
            </div>
          </UCard>
          <div class="space-y-5">
            <UAlert
              v-if="!structureEditable"
              color="warning"
              :title="t('automation.editor.compositeTitle')"
              :description="t('automation.editor.compositeDescription')"
            />
            <UCard>
              <template #header>
                <div>
                  <h2 class="font-semibold">
                    {{ t('automation.editor.title') }}
                  </h2><p class="text-sm text-muted">
                    {{ t('automation.editor.description') }}
                  </p>
                </div>
              </template>
              <UForm class="space-y-4" :state="form" @submit="controller.save(draft())">
                <div class="grid gap-4 md:grid-cols-2">
                  <UFormField :label="t('automation.editor.ruleId')" required>
                    <UInput v-model="form.id" :disabled="controller.selected.value !== null || controller.isMutating.value" />
                  </UFormField><UFormField :label="t('automation.editor.name')" required>
                    <UInput v-model="form.name" :disabled="controller.isMutating.value" />
                  </UFormField><UFormField :label="t('automation.editor.trigger')">
                    <USelect v-model="form.trigger" :items="triggerItems" />
                  </UFormField><UFormField :label="t('automation.editor.enabled')">
                    <USwitch v-model="form.isEnabled" />
                  </UFormField><UFormField :label="t('automation.editor.conditionField')">
                    <UInput v-model="form.fieldKey" />
                  </UFormField><UFormField :label="t('automation.editor.conditionOperator')">
                    <USelect v-model="form.operator" :items="operatorItems" />
                  </UFormField><UFormField :label="t('automation.editor.conditionValue')">
                    <UInput v-model="form.scalarValue" />
                  </UFormField><UFormField :label="t('automation.editor.action')">
                    <USelect v-model="form.actionType" :items="actionItems" />
                  </UFormField><UFormField :label="t('automation.editor.target')">
                    <USelect v-model="form.targetKind" :items="targetItems" />
                  </UFormField><UFormField v-if="form.targetKind === 'StablePlayer' || form.targetKind === 'DiscordTarget'" :label="t('automation.editor.targetReference')">
                    <UInput v-model="form.targetReference" />
                  </UFormField><UFormField v-if="form.actionType !== 'AdjustEconomy'" :label="t('automation.editor.actionValue')">
                    <UInput v-model="form.actionValue" />
                  </UFormField><UFormField v-if="form.actionType === 'GrantItem' || form.actionType === 'AdjustEconomy'" :label="t('automation.editor.amount')">
                    <UInputNumber v-model="form.amount" />
                  </UFormField><UFormField v-if="form.actionType === 'MutePlayer'" :label="t('automation.editor.muteSeconds')">
                    <UInputNumber v-model="form.durationSeconds" :min="1" />
                  </UFormField><UFormField :label="t('automation.editor.cooldownSeconds')">
                    <UInputNumber v-model="form.cooldownSeconds" :min="0" />
                  </UFormField>
                </div>
                <div class="flex flex-wrap justify-end gap-2">
                  <UButton
                    v-if="controller.selected.value"
                    color="error"
                    :label="t('automation.common.delete')"
                    type="button"
                    variant="soft"
                    :disabled="controller.isMutating.value"
                    @click="requestRemoveSelected"
                  /><UButton
                    color="neutral"
                    :label="t('automation.editor.validate')"
                    type="button"
                    variant="outline"
                    :loading="controller.isMutating.value"
                    :disabled="!structureEditable"
                    @click="controller.validate(draft())"
                  /><UButton
                    :label="t('automation.common.save')"
                    type="submit"
                    :loading="controller.isMutating.value"
                    :disabled="!structureEditable"
                  />
                </div>
              </UForm>
              <UAlert
                v-if="controller.validation.value"
                class="mt-4"
                :color="controller.validation.value.isValid ? 'success' : 'warning'"
                :title="t(controller.validation.value.isValid ? 'automation.validation.valid' : 'automation.validation.invalid')"
                :description="controller.validation.value.issues.map(issue => `${issue.path}: ${issue.code}`).join('\n') || t('automation.validation.noIssues')"
              />
            </UCard>
            <UCard v-if="controller.executionState.value === 'available'">
              <template #header>
                <h2 class="font-semibold">
                  {{ t('automation.execution.title') }}
                </h2>
              </template>
              <div v-if="controller.executions.value.length" class="space-y-2">
                <div v-for="execution in controller.executions.value" :key="execution.executionId" class="flex flex-wrap items-center justify-between gap-2 text-sm">
                  <span>{{ execution.ruleId }}</span>
                  <UBadge :color="operationStatus(execution.status).tone" :label="t(operationStatus(execution.status).i18nKey)" variant="subtle" />
                </div>
              </div>
              <p v-else class="text-sm text-muted">
                {{ t('automation.execution.empty') }}
              </p>
            </UCard>
            <UCard>
              <template #header>
                <div>
                  <h2 class="font-semibold">
                    {{ t('automation.dryRun.title') }}
                  </h2><p class="text-sm text-muted">
                    {{ t('automation.dryRun.description') }}
                  </p>
                </div>
              </template><div class="grid gap-4 md:grid-cols-2">
                <UFormField :label="t('automation.dryRun.triggerId')">
                  <UInput v-model="snapshot.triggerId" />
                </UFormField><UFormField :label="t('automation.dryRun.occurredAtUtc')">
                  <UInput v-model="snapshot.occurredAtUtc" />
                </UFormField><UFormField :label="t('automation.dryRun.crossplatformId')">
                  <UInput v-model="snapshot.crossplatformId" />
                </UFormField><UFormField v-if="form.trigger === 'ChatMessage'" :label="t('automation.dryRun.chatText')">
                  <UInput v-model="snapshot.chatText" />
                </UFormField><UFormField v-if="form.trigger === 'Cron'" :label="t('automation.dryRun.scheduledForUtc')">
                  <UInput v-model="snapshot.scheduledForUtc" />
                </UFormField><UFormField v-if="form.trigger === 'BloodMoonPhaseEntered'" :label="t('automation.dryRun.bloodMoonPhase')">
                  <UInput v-model="snapshot.phase" />
                </UFormField><UFormField :label="t('automation.dryRun.gapIds')">
                  <UInput v-model="snapshot.gapIds" />
                </UFormField>
              </div><div class="mt-4 flex justify-end">
                <UButton
                  :label="t('automation.dryRun.run')"
                  :loading="controller.isMutating.value"
                  :disabled="!structureEditable"
                  @click="controller.dryRun(draft(), triggerSnapshot())"
                />
              </div><UAlert
                v-if="controller.dryRunResult.value"
                class="mt-4"
                :color="controller.dryRunResult.value.validation.isValid ? 'success' : 'warning'"
                :title="t('automation.dryRun.result', { result: controller.dryRunResult.value.evaluation?.truth ?? t('automation.dryRun.notEvaluated') })"
                :description="t('automation.dryRun.plannedActions', { count: controller.dryRunResult.value.plannedActions.length })"
              />
            </UCard>
          </div>
        </div>
      </UContainer>
    </template>
  </UDashboardPanel>
  <UModal
    v-model:open="deleteConfirmationOpen"
    :title="t('automation.confirmDelete', { name: controller.selected.value?.name ?? '' })"
  >
    <template #footer>
      <div class="flex w-full justify-end gap-2">
        <UButton
          color="neutral"
          :label="t('common.cancel')"
          variant="outline"
          @click="deleteConfirmationOpen = false"
        />
        <UButton
          color="error"
          :label="t('automation.common.delete')"
          :loading="controller.isMutating.value"
          @click="removeSelected"
        />
      </div>
    </template>
  </UModal>
</template>

<script setup lang="ts">
import type { AutomationCondition, AutomationConditionKind, AutomationConditionOperator, AutomationPredicate } from '../api/automation'

import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { createPredicateCondition } from '../model/useAutomationEditor'

const props = withDefaults(defineProps<{ condition: AutomationCondition, depth?: number, nodeCount?: number, removable?: boolean }>(), { depth: 0, nodeCount: 1, removable: false })
const emit = defineEmits<{ update: [value: AutomationCondition], remove: [] }>()
const { t } = useI18n()

const kindItems = computed(() => (['All', 'Any', 'Not', 'Predicate'] as const).map(value => ({ label: t(`automation.condition.kind.${value}`), value })))
const operatorItems = computed(() => (['Equals', 'NotEquals', 'InSet', 'NumberRange', 'TimeWindow', 'PlayerGroup', 'Permission', 'Cooldown'] as const).map(value => ({ label: t(`automation.enums.operator.${value}`), value })))
const canAdd = computed(() => props.condition.kind !== 'Predicate' && props.depth < 4 && props.nodeCount < 64 && (props.condition.kind !== 'Not' || (props.condition.children?.length ?? 0) === 0))

function updateKind(kind: AutomationConditionKind) {
  if (kind === 'Predicate') {
    emit('update', Object.freeze({ ...createPredicateCondition(), nodeId: props.condition.nodeId }))
    return
  }
  emit('update', Object.freeze({ nodeId: props.condition.nodeId, kind, children: Object.freeze(kind === 'Not' ? [createPredicateCondition()] : []) }))
}
function updatePredicate(patch: Partial<AutomationPredicate>) {
  const current = props.condition.predicate
  if (current === undefined) return
  emit('update', Object.freeze({ ...props.condition, predicate: Object.freeze({ ...current, ...patch }) }))
}
function updateOperator(operator: AutomationConditionOperator) {
  const fieldKey = props.condition.predicate?.fieldKey ?? ''
  const predicate: AutomationPredicate = operator === 'InSet'
    ? { fieldKey, operator, setValues: [] }
    : operator === 'NumberRange'
      ? { fieldKey, operator, minimumInclusive: 0, maximumInclusive: 0 }
      : operator === 'TimeWindow'
        ? { fieldKey, operator, window: { timeZoneId: 'UTC', startInclusive: { hour: 0, minute: 0 }, endInclusive: { hour: 23, minute: 59 } } }
        : operator === 'Cooldown'
          ? { fieldKey, operator }
          : { fieldKey, operator, scalarValue: '' }
  emit('update', Object.freeze({ ...props.condition, predicate: Object.freeze(predicate) }))
}
function updateChild(index: number, child: AutomationCondition) {
  const children = [...(props.condition.children ?? [])]
  children[index] = child
  emit('update', Object.freeze({ ...props.condition, children: Object.freeze(children) }))
}
function removeChild(index: number) {
  emit('update', Object.freeze({ ...props.condition, children: Object.freeze((props.condition.children ?? []).filter((_, childIndex) => childIndex !== index)) }))
}
function addChild() {
  if (!canAdd.value) return
  emit('update', Object.freeze({ ...props.condition, children: Object.freeze([...(props.condition.children ?? []), createPredicateCondition()]) }))
}
</script>

<template>
  <div class="space-y-3 rounded-md border border-muted bg-elevated/40 p-3" :data-condition-id="condition.nodeId">
    <div class="flex flex-wrap items-end gap-2">
      <UFormField :label="t('automation.condition.kindLabel')" class="min-w-40 flex-1">
        <USelect :model-value="condition.kind" :items="kindItems" @update:model-value="updateKind($event as AutomationConditionKind)" />
      </UFormField>
      <UBadge color="neutral" variant="subtle" :label="condition.kind" />
      <UButton v-if="removable" color="error" icon="i-lucide-trash-2" :label="t('automation.condition.remove')" variant="ghost" @click="emit('remove')" />
    </div>

    <template v-if="condition.kind === 'Predicate' && condition.predicate">
      <div class="grid gap-3 md:grid-cols-2">
        <UFormField :label="t('automation.editor.conditionField')">
          <UInput :model-value="condition.predicate.fieldKey" @update:model-value="updatePredicate({ fieldKey: String($event) })" />
        </UFormField>
        <UFormField :label="t('automation.editor.conditionOperator')">
          <USelect :model-value="condition.predicate.operator" :items="operatorItems" @update:model-value="updateOperator($event as AutomationConditionOperator)" />
        </UFormField>
        <UFormField v-if="['Equals', 'NotEquals', 'PlayerGroup', 'Permission'].includes(condition.predicate.operator)" :label="t('automation.editor.conditionValue')">
          <UInput :model-value="condition.predicate.scalarValue ?? ''" @update:model-value="updatePredicate({ scalarValue: String($event) })" />
        </UFormField>
        <UFormField v-if="condition.predicate.operator === 'InSet'" :label="t('automation.condition.setValues')">
          <UInput :model-value="condition.predicate.setValues?.join(', ') ?? ''" @update:model-value="updatePredicate({ setValues: String($event).split(',').map(value => value.trim()).filter(Boolean) })" />
        </UFormField>
        <template v-if="condition.predicate.operator === 'NumberRange'">
          <UFormField :label="t('automation.condition.minimum')"><UInputNumber :model-value="condition.predicate.minimumInclusive" @update:model-value="updatePredicate({ minimumInclusive: Number($event) })" /></UFormField>
          <UFormField :label="t('automation.condition.maximum')"><UInputNumber :model-value="condition.predicate.maximumInclusive" @update:model-value="updatePredicate({ maximumInclusive: Number($event) })" /></UFormField>
        </template>
        <template v-if="condition.predicate.operator === 'TimeWindow' && condition.predicate.window">
          <UFormField :label="t('automation.condition.timeZone')"><UInput :model-value="condition.predicate.window.timeZoneId" @update:model-value="updatePredicate({ window: { ...condition.predicate!.window!, timeZoneId: String($event) } })" /></UFormField>
          <div class="grid grid-cols-2 gap-2">
            <UFormField :label="t('automation.condition.startHour')"><UInputNumber :model-value="condition.predicate.window.startInclusive.hour" :min="0" :max="23" @update:model-value="updatePredicate({ window: { ...condition.predicate!.window!, startInclusive: { ...condition.predicate!.window!.startInclusive, hour: Number($event) } } })" /></UFormField>
            <UFormField :label="t('automation.condition.endHour')"><UInputNumber :model-value="condition.predicate.window.endInclusive.hour" :min="0" :max="23" @update:model-value="updatePredicate({ window: { ...condition.predicate!.window!, endInclusive: { ...condition.predicate!.window!.endInclusive, hour: Number($event) } } })" /></UFormField>
          </div>
        </template>
      </div>
    </template>

    <template v-else>
      <div class="space-y-3 border-s border-muted ps-3">
        <AutomationConditionTreeEditor
          v-for="(child, index) in condition.children ?? []"
          :key="child.nodeId"
          :condition="child"
          :depth="depth + 1"
          :node-count="nodeCount"
          removable
          @update="updateChild(index, $event)"
          @remove="removeChild(index)"
        />
      </div>
      <UButton color="neutral" icon="i-lucide-plus" :label="t('automation.condition.addChild')" variant="outline" :disabled="!canAdd" @click="addChild" />
    </template>
  </div>
</template>

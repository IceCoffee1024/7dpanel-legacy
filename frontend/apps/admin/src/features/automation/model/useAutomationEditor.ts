import type { DeepReadonly, Ref } from 'vue'
import type { AutomationAction, AutomationCondition, AutomationRule, AutomationRuleDraft, AutomationTriggerSnapshot } from '../api/automation'

import { readonly, shallowRef, watch } from 'vue'

let editorId = 0
function nextId(prefix: string) {
  editorId += 1
  return `${prefix}-${editorId}`
}

export function createPredicateCondition(): AutomationCondition {
  return Object.freeze({ nodeId: nextId('condition'), kind: 'Predicate', predicate: Object.freeze({ fieldKey: 'actor.group', operator: 'Equals', scalarValue: '' }) })
}

export function createAutomationAction(): AutomationAction {
  return Object.freeze({ id: nextId('action'), type: 'PrivateMessage', target: Object.freeze({ kind: 'TriggerPlayer' }), privateMessage: Object.freeze({ message: '' }) })
}

function createDraft(rule: AutomationRule | null): AutomationRuleDraft {
  if (rule !== null) {
    return structuredClone({
      id: rule.id,
      expectedVersion: rule.version,
      name: rule.name,
      isEnabled: rule.isEnabled,
      trigger: rule.trigger,
      condition: rule.condition,
      actions: rule.actions,
      cooldownSeconds: rule.cooldownSeconds,
      cooldownScope: rule.cooldownScope,
      concurrencyPolicy: rule.concurrencyPolicy,
      failurePolicy: rule.failurePolicy,
    })
  }
  return {
    id: '',
    name: '',
    isEnabled: true,
    trigger: { type: 'PlayerJoined' },
    condition: createPredicateCondition(),
    actions: [createAutomationAction()],
    cooldownSeconds: 0,
    cooldownScope: 'RulePlayer',
    concurrencyPolicy: 'SkipIfRunning',
    failurePolicy: 'StopOnFailure',
  }
}

function createSnapshot(triggerType: AutomationRuleDraft['trigger']['type']): AutomationTriggerSnapshot {
  return {
    triggerId: '',
    trigger: { type: triggerType },
    occurredAtUtc: new Date().toISOString(),
    actor: {},
    gapIds: [],
  }
}

export function useAutomationEditor(selected: Readonly<Ref<AutomationRule | null>>) {
  const draft = shallowRef<AutomationRuleDraft>(createDraft(selected.value))
  const snapshot = shallowRef<AutomationTriggerSnapshot>(createSnapshot(draft.value.trigger.type))

  watch(selected, rule => reset(rule))

  function reset(rule: AutomationRule | null) {
    draft.value = createDraft(rule)
    snapshot.value = createSnapshot(draft.value.trigger.type)
  }
  function updateDraft(value: AutomationRuleDraft) {
    const triggerChanged = value.trigger.type !== draft.value.trigger.type
    draft.value = value
    if (triggerChanged)
      snapshot.value = createSnapshot(value.trigger.type)
  }
  function updateSnapshot(value: AutomationTriggerSnapshot) {
    snapshot.value = value
  }

  return { draft: readonly(draft) as DeepReadonly<typeof draft>, snapshot: readonly(snapshot) as DeepReadonly<typeof snapshot>, reset, updateDraft, updateSnapshot }
}

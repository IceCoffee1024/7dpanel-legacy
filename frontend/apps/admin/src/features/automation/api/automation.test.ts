import { beforeEach, describe, expect, it, vi } from 'vitest'

import { dryRunAutomationRule, parseAutomationRules, queryAutomationExecutions } from './automation'

const requestJson = vi.hoisted(() => vi.fn())

vi.mock('../../../shared/api/http', () => ({ requestJson }))

describe('automation API parser', () => {
  beforeEach(() => requestJson.mockReset())

  it('accepts the fixed rule contract and rejects secret-like response fields', () => {
    const rule = {
      id: 'welcome',
      version: 1,
      name: 'Welcome players',
      isEnabled: true,
      trigger: { type: 'PlayerJoined' },
      condition: {
        nodeId: 'root',
        kind: 'Predicate',
        predicate: { fieldKey: 'actor.group', operator: 'Equals', scalarValue: 'member' },
      },
      actions: [{
        id: 'hello',
        type: 'PrivateMessage',
        target: { kind: 'TriggerPlayer' },
        privateMessage: { message: 'Welcome' },
      }],
      cooldownSeconds: 30,
      cooldownScope: 'RulePlayer',
      concurrencyPolicy: 'SkipIfRunning',
      failurePolicy: 'StopOnFailure',
      createdAtUtc: '2026-07-27T00:00:00+00:00',
      updatedAtUtc: '2026-07-27T00:00:00+00:00',
    }

    expect(parseAutomationRules([rule])).toHaveLength(1)
    expect(() => parseAutomationRules([{ ...rule, botToken: 'secret' }])).toThrow('Invalid server protocol')
  })

  it('strictly parses execution summaries and per-action evidence', async () => {
    requestJson.mockResolvedValueOnce([{
      executionId: 'execution-1',
      ruleId: 'welcome',
      triggerId: 'trigger-1',
      status: 'Failed',
      correlationId: 'correlation-1',
      startedAtUtc: '2026-07-27T00:00:00+00:00',
      completedAtUtc: '2026-07-27T00:00:01+00:00',
      errorCode: 'action_failed',
      conditions: [{ nodeId: 'root', truth: 'Matched' }],
      actions: [{
        ordinal: 0,
        actionType: 'PrivateMessage',
        status: 'Failed',
        errorCode: 'target_unavailable',
        startedAtUtc: '2026-07-27T00:00:00+00:00',
        completedAtUtc: '2026-07-27T00:00:01+00:00',
      }],
    }])

    const executions = await queryAutomationExecutions('Bearer owner')

    expect(executions[0]).toMatchObject({
      executionId: 'execution-1',
      status: 'Failed',
      actions: [{ ordinal: 0, status: 'Failed', errorCode: 'target_unavailable' }],
    })
    expect(Object.isFrozen(executions[0])).toBe(true)

    requestJson.mockResolvedValueOnce([{
      executionId: 'execution-2',
      ruleId: 'welcome',
      triggerId: 'trigger-2',
      status: 'Invented',
      correlationId: 'correlation-2',
      startedAtUtc: null,
      completedAtUtc: null,
      errorCode: null,
      conditions: [],
      actions: [],
    }])
    await expect(queryAutomationExecutions('Bearer owner')).rejects.toThrow('Invalid server protocol')
  })

  it('accepts the backend wouldExecute dry-run field without weakening strict parsing', async () => {
    requestJson.mockResolvedValueOnce({
      validation: { isValid: true, issues: [] },
      evaluation: { truth: 'Matched', trace: [{ nodeId: 'root', truth: 'Matched', isValueKnown: true }] },
      plannedActions: [{
        ordinal: 0,
        actionId: 'hello',
        actionType: 'PrivateMessage',
        dependency: { status: 'Available' },
        target: { isResolved: true },
        wouldExecute: true,
      }],
    })

    const result = await dryRunAutomationRule('Bearer owner', {} as never, {} as never)

    expect(result.plannedActions[0]).toMatchObject({ wouldExecute: true })
  })
})

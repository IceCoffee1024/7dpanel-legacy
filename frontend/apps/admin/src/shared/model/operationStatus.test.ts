import { describe, expect, it } from 'vitest'

import { operationStatus } from './operationStatus'

describe('operationStatus', () => {
  it.each([
    ['queued', 'queued', 'info', false, false],
    ['running', 'running', 'info', false, false],
    ['succeeded', 'succeeded', 'success', true, false],
    ['failed', 'failed', 'error', true, true],
    ['cancelled', 'cancelled', 'neutral', true, true],
  ] as const)('projects %s into the common operation vocabulary', (value, semantic, tone, terminal, safeToRetry) => {
    expect(operationStatus(value)).toMatchObject({
      semantic,
      i18nKey: `operationStatus.${semantic}`,
      tone,
      terminal,
      safeToRetry,
      protocolError: null,
    })
  })

  it.each([
    ['Interrupted', 'interrupted'],
    ['ResultUnknown', 'result-unknown'],
    ['RollbackFailed', 'rollback-failed'],
    ['Unavailable', 'unavailable'],
  ] as const)('preserves the dangerous extension %s', (value, semantic) => {
    const presentation = operationStatus(value)

    expect(presentation.semantic).toBe(semantic)
    expect(presentation.safeToRetry).toBe(false)
    expect(presentation.tone).not.toBe('success')
  })

  it('renders and records an unknown protocol status without inferring an outcome', () => {
    expect(operationStatus('FutureBackendState')).toEqual({
      semantic: 'unknown',
      i18nKey: 'operationStatus.unknown',
      tone: 'error',
      terminal: false,
      safeToRetry: false,
      protocolError: { code: 'unknown_operation_status', received: 'FutureBackendState' },
    })
  })
})

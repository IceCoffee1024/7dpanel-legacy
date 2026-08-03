import { afterEach, describe, expect, it, vi } from 'vitest'

import { requestJson } from '../../../shared/api/http'
import {
  getServerOperation,
  parseServerOperationStatus,
  restartServer,
  ServerOperationError,
  shutdownServer,
} from './serverOperations'

vi.mock('../../../shared/api/http', () => ({
  requestJson: vi.fn(),
}))

const restartAccepted = {
  operationId: 'restart-1',
  code: 'restart_script_started',
  requestedAtUtc: '2026-07-25T01:02:03Z',
  scriptStartedAtUtc: '2026-07-25T01:02:04+00:00',
  auditStatus: 'recorded',
}

const shutdownAccepted = {
  operationId: 'shutdown-1',
  code: 'shutdown_requested',
  requestedAtUtc: '2026-07-25T01:02:03Z',
  acceptedAtUtc: '2026-07-25T01:02:04Z',
  auditStatus: 'audit_degraded',
}

describe('server operation API', () => {
  afterEach(() => vi.clearAllMocks())

  it('posts only fixed restart confirmation and requires HTTP 202', async () => {
    vi.mocked(requestJson).mockResolvedValue(restartAccepted)
    const controller = new AbortController()

    await expect(restartServer('Bearer owner', controller.signal)).resolves.toEqual(restartAccepted)

    expect(requestJson).toHaveBeenCalledExactlyOnceWith('/api/v1/server-operations/restart', {
      body: JSON.stringify({ confirmed: true }),
      expectedStatus: 202,
      headers: {
        'Authorization': 'Bearer owner',
        'Content-Type': 'application/json',
      },
      method: 'POST',
      signal: controller.signal,
    })
    expect(vi.mocked(requestJson).mock.calls[0]?.[1]).not.toEqual(expect.objectContaining({
      actor: expect.anything(),
      command: expect.anything(),
      env: expect.anything(),
      parameters: expect.anything(),
      path: expect.anything(),
    }))
  })

  it('posts only fixed shutdown confirmation and uses independent success semantics', async () => {
    vi.mocked(requestJson).mockResolvedValue(shutdownAccepted)

    await expect(shutdownServer('Bearer owner')).resolves.toEqual(shutdownAccepted)

    expect(requestJson).toHaveBeenCalledExactlyOnceWith('/api/v1/server-operations/shutdown', {
      body: JSON.stringify({ confirmed: true }),
      expectedStatus: 202,
      headers: {
        'Authorization': 'Bearer owner',
        'Content-Type': 'application/json',
      },
      method: 'POST',
      signal: undefined,
    })
  })

  it.each([
    ['restart with shutdown code', restartServer, { ...restartAccepted, code: 'shutdown_requested' }],
    ['restart with unknown audit status', restartServer, { ...restartAccepted, auditStatus: 'complete' }],
    ['restart with an invalid timestamp', restartServer, { ...restartAccepted, scriptStartedAtUtc: 'today' }],
    ['restart missing an operation id', restartServer, { ...restartAccepted, operationId: undefined }],
    ['shutdown with restart code', shutdownServer, { ...shutdownAccepted, code: 'restart_script_started' }],
    ['shutdown with unknown status', shutdownServer, { ...shutdownAccepted, code: 'shutdown_complete' }],
    ['shutdown with a non-UTC timestamp', shutdownServer, { ...shutdownAccepted, acceptedAtUtc: '2026-07-25T09:02:04+08:00' }],
  ])('rejects %s with a stable safe error', async (_name, request, response) => {
    vi.mocked(requestJson).mockResolvedValue(response)

    const error = await request('Bearer owner').catch(value => value)

    expect(error).toBeInstanceOf(ServerOperationError)
    expect(error).toMatchObject({ code: 'invalid-response', message: 'Invalid server operation response' })
    expect(error).not.toHaveProperty('detail')
  })

  it('preserves safe Problem metadata including 401 for the shared session flow', async () => {
    const problem = Object.assign(new Error('HTTP request failed with status 401'), {
      code: 'http',
      problemCode: 'authentication_required',
      status: 401,
    })
    vi.mocked(requestJson).mockRejectedValue(problem)

    await expect(restartServer('Bearer expired')).rejects.toBe(problem)
    await expect(shutdownServer('Bearer expired')).rejects.toBe(problem)
  })

  it('gets a sanitized persisted operation status through its stable ID', async () => {
    const operation = {
      operationId: 'restart-1',
      kind: 'restart_script',
      status: 'running',
      requestedAtUtc: '2026-07-25T01:02:03Z',
      startedAtUtc: '2026-07-25T01:02:04Z',
      completedAtUtc: null,
      completionDeadlineUtc: '2026-07-25T01:07:04Z',
      failureCode: null,
      auditStatus: 'recorded',
    }
    vi.mocked(requestJson).mockResolvedValue(operation)
    const controller = new AbortController()

    await expect(getServerOperation('Bearer owner', 'restart-1', controller.signal)).resolves.toEqual(operation)

    expect(requestJson).toHaveBeenCalledWith('/api/v1/server-operations/restart-1', {
      expectedStatus: 200,
      headers: { 'Authorization': 'Bearer owner' },
      method: 'GET',
      signal: controller.signal,
    })
  })

  it('rejects sensitive or unsupported fields from operation status responses', () => {
    expect(() => parseServerOperationStatus({
      operationId: 'restart-1', kind: 'restart_script', status: 'running',
      requestedAtUtc: '2026-07-25T01:02:03Z', startedAtUtc: '2026-07-25T01:02:04Z',
      completedAtUtc: null, completionDeadlineUtc: '2026-07-25T01:07:04Z',
      failureCode: null, auditStatus: 'recorded', scriptPath: 'C:\\private\\restart.cmd',
    })).toThrow(ServerOperationError)
  })
})

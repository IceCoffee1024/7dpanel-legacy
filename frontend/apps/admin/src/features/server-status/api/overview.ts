import type { OverviewSnapshot } from '../model/overview'

import { requestJson } from '../../../shared/api/http'
import { parseOverview } from '../model/overview'

export async function fetchOverview(
  authorizationHeader: string,
  signal?: AbortSignal,
): Promise<OverviewSnapshot> {
  const response = await requestJson<unknown>('/api/v1/overview', {
    headers: { Authorization: authorizationHeader },
    method: 'GET',
    signal,
  })
  return parseOverview(response)
}

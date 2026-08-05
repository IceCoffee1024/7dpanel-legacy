import type { City, CityInput } from './community.types'

import { requestJson } from '../../../shared/api/http'

import {
  bool,
  collection,
  ensureChronology,
  headers,
  integer,
  invalid,
  long,
  parseWorldPosition,
  queryPath,
  record,
  text,
  utc,
} from './community.protocol'

const cityKeys = ['cityId', 'name', 'description', 'enabled', 'position', 'sortOrder', 'createdAtUtc', 'updatedAtUtc', 'rowVersion'] as const

export function parseCity(value: unknown): City {
  const source = record(value, cityKeys)
  const createdAtUtc = utc(source.createdAtUtc)
  const updatedAtUtc = utc(source.updatedAtUtc)
  ensureChronology(createdAtUtc, updatedAtUtc)
  return Object.freeze({
    cityId: text(source.cityId),
    name: text(source.name),
    description: text(source.description, true),
    enabled: bool(source.enabled),
    position: parseWorldPosition(source.position),
    sortOrder: integer(source.sortOrder),
    createdAtUtc,
    updatedAtUtc,
    rowVersion: long(source.rowVersion),
  })
}

export async function fetchCities(authorization: string, signal?: AbortSignal): Promise<readonly City[]> {
  const result = collection(await requestJson<unknown>(queryPath('/api/v1/community/cities', { enabledOnly: true }), {
    headers: headers(authorization),
    expectedStatus: 200,
    signal,
  }), parseCity)
  if (result.some(value => !value.enabled))
    return invalid()
  return result
}

export async function fetchAllCities(authorization: string, signal?: AbortSignal): Promise<readonly City[]> {
  return collection(await requestJson<unknown>(queryPath('/api/v1/community/cities', { enabledOnly: false }), {
    headers: headers(authorization),
    expectedStatus: 200,
    signal,
  }), parseCity)
}

export async function upsertCity(authorization: string, input: CityInput, signal?: AbortSignal): Promise<City> {
  const response = await requestJson<unknown>(`/api/v1/community/cities/${encodeURIComponent(input.cityId)}`, {
    method: 'PUT',
    headers: headers(authorization, true),
    body: JSON.stringify({
      name: input.name,
      description: input.description,
      enabled: input.enabled,
      position: input.position,
      sortOrder: input.sortOrder,
    }),
    expectedStatus: 200,
    signal,
  })
  const authoritative = parseCity(response)
  if (authoritative.cityId !== input.cityId)
    return invalid()
  return authoritative
}

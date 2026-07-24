import { describe, expect, it } from 'vitest'

import {
  formatDeviceType,
  formatDurationMinutes,
  formatNullable,
  formatPosition,
  formatRoundedNumber,
} from './onlinePlayerFormatting'

describe('onlinePlayerFormatting', () => {
  it('formats nullable transport values as unknown', () => {
    expect(formatNullable(null)).toBe('未知')
    expect(formatNullable('Steam_123')).toBe('Steam_123')
  })

  it('rounds coordinates and numbers with the chosen locale', () => {
    expect(formatRoundedNumber(127540.75, 'zh-CN')).toBe('127,541')
    expect(formatPosition({ x: 100.5, y: -1.5, z: 200.25 }, 'zh-CN')).toBe('101, -1, 200')
  })

  it.each([
    [0.49, '少于 1 分钟'],
    [1, '1 分钟'],
    [60, '1 小时'],
    [134.5, '2 小时 15 分钟'],
    [1_440, '1 天'],
    [4823.5, '3 天 8 小时 24 分钟'],
  ])('formats %s minutes', (value, expected) => {
    expect(formatDurationMinutes(value, 'zh-CN')).toBe(expected)
  })

  it.each([
    ['linux', 'Linux'],
    ['mac', 'macOS'],
    ['windows', 'Windows'],
    ['playStation', 'PlayStation'],
    ['xbox', 'Xbox'],
    ['unknown', 'Unknown'],
  ] as const)('formats %s device type', (value, expected) => {
    expect(formatDeviceType(value)).toBe(expected)
  })
})

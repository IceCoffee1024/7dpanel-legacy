import { describe, expect, it } from 'vitest'

import { matchSupportedLocale, negotiateLocale } from './locale'

describe('matchSupportedLocale', () => {
  it.each([
    ['en', 'en'],
    ['EN-us', 'en'],
    ['zh', 'zh-CN'],
    ['zh-CN', 'zh-CN'],
    ['zh-SG', 'zh-CN'],
    ['zh-Hans', 'zh-CN'],
    ['zh-Hans-CN', 'zh-CN'],
  ] as const)('maps %s to %s', (tag, expected) => {
    expect(matchSupportedLocale(tag)).toBe(expected)
  })

  it.each([
    'zh-TW',
    'zh-HK',
    'zh-MO',
    'zh-Hant',
    'zh-Hant-HK',
    'fr-FR',
    'not_a_locale',
  ])('does not map unsupported locale %s', (tag) => {
    expect(matchSupportedLocale(tag)).toBeNull()
  })
})

describe('negotiateLocale', () => {
  it('uses the first supported browser preference', () => {
    expect(negotiateLocale(['zh-TW', 'en-US'])).toBe('en')
    expect(negotiateLocale(['fr-FR', 'zh-Hans'])).toBe('zh-CN')
  })

  it('falls back to English when no preference is supported', () => {
    expect(negotiateLocale(['fr-FR', 'zh-Hant'])).toBe('en')
    expect(negotiateLocale([])).toBe('en')
  })
})

export type SupportedLocale = 'en' | 'zh-CN'

export const DEFAULT_LOCALE: SupportedLocale = 'en'

const TRADITIONAL_CHINESE_REGIONS = new Set(['HK', 'MO', 'TW'])
const SIMPLIFIED_CHINESE_REGIONS = new Set(['CN', 'SG'])

export function matchSupportedLocale(tag: string): SupportedLocale | null {
  try {
    const locale = new Intl.Locale(tag)

    if (locale.language === 'en')
      return 'en'

    if (locale.language !== 'zh')
      return null

    if (locale.script === 'Hant' || TRADITIONAL_CHINESE_REGIONS.has(locale.region ?? ''))
      return null

    if (locale.script === 'Hans' || SIMPLIFIED_CHINESE_REGIONS.has(locale.region ?? ''))
      return 'zh-CN'

    return locale.script === undefined && locale.region === undefined
      ? 'zh-CN'
      : null
  }
  catch {
    return null
  }
}

export function negotiateLocale(tags: readonly string[]): SupportedLocale {
  for (const tag of tags) {
    const locale = matchSupportedLocale(tag)
    if (locale !== null)
      return locale
  }

  return DEFAULT_LOCALE
}

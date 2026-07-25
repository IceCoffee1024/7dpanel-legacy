export function formatBytes(value: number | null, locale: string): string {
  if (value === null)
    return '—'
  const units = ['B', 'KiB', 'MiB', 'GiB', 'TiB']
  let amount = value
  let unit = 0
  while (amount >= 1024 && unit < units.length - 1) {
    amount /= 1024
    unit++
  }
  return `${new Intl.NumberFormat(locale, { maximumFractionDigits: 1 }).format(amount)} ${units[unit]}`
}

export interface DurationLabels {
  day: (count: number) => string
  hour: (count: number) => string
  minute: (count: number) => string
}

export function formatDuration(seconds: number | null, labels: DurationLabels): string {
  if (seconds === null)
    return '—'
  const days = Math.floor(seconds / 86_400)
  const hours = Math.floor((seconds % 86_400) / 3_600)
  const minutes = Math.floor((seconds % 3_600) / 60)
  return [days > 0 ? labels.day(days) : '', hours > 0 ? labels.hour(hours) : '', labels.minute(minutes)]
    .filter(Boolean)
    .join(' ')
}

export function usedPercent(used: number | null, total: number | null): number | null {
  if (used === null || total === null || total <= 0)
    return null
  return Math.min(100, Math.max(0, (used / total) * 100))
}

export function formatNumber(value: number | null, locale: string, digits = 1): string {
  return value === null ? '—' : new Intl.NumberFormat(locale, { maximumFractionDigits: digits }).format(value)
}

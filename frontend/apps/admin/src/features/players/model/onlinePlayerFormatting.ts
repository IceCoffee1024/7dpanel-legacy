import type { OnlinePlayerDeviceType, OnlinePlayerPosition } from '../api/onlinePlayers'

export function formatNullable(value: string | null): string {
  return value ?? '未知'
}

export function formatRoundedNumber(value: number, locale = 'zh-CN'): string {
  return new Intl.NumberFormat(locale, { maximumFractionDigits: 0 }).format(value)
}

export function formatPosition(position: OnlinePlayerPosition, locale = 'zh-CN'): string {
  return [position.x, position.y, position.z]
    .map(value => formatRoundedNumber(Math.round(value), locale))
    .join(', ')
}

export function formatDurationMinutes(value: number, locale = 'zh-CN'): string {
  const totalMinutes = Math.round(value)
  const isChinese = locale.toLowerCase().startsWith('zh')
  if (totalMinutes < 1)
    return isChinese ? '少于 1 分钟' : 'Less than 1 minute'

  const days = Math.floor(totalMinutes / 1_440)
  const hours = Math.floor((totalMinutes % 1_440) / 60)
  const minutes = totalMinutes % 60
  const parts: string[] = []
  if (days > 0)
    parts.push(isChinese ? `${days} 天` : `${days} ${days === 1 ? 'day' : 'days'}`)
  if (hours > 0)
    parts.push(isChinese ? `${hours} 小时` : `${hours} ${hours === 1 ? 'hour' : 'hours'}`)
  if (minutes > 0)
    parts.push(isChinese ? `${minutes} 分钟` : `${minutes} ${minutes === 1 ? 'minute' : 'minutes'}`)
  return parts.join(' ')
}

export function formatDeviceType(value: OnlinePlayerDeviceType): string {
  switch (value) {
    case 'linux': return 'Linux'
    case 'mac': return 'macOS'
    case 'windows': return 'Windows'
    case 'playStation': return 'PlayStation'
    case 'xbox': return 'Xbox'
    default: return 'Unknown'
  }
}

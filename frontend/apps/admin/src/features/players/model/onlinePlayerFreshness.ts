const staleObservationAgeMs = 90_000

export function isOnlinePlayerObservationStale(
  observedAtUtc: string,
  now = Date.now(),
): boolean {
  return now - Date.parse(observedAtUtc) > staleObservationAgeMs
}

export function formatOnlinePlayerObservedAt(observedAtUtc: string): string {
  return new Intl.DateTimeFormat('zh-CN', {
    dateStyle: 'short',
    timeStyle: 'medium',
  }).format(new Date(observedAtUtc))
}

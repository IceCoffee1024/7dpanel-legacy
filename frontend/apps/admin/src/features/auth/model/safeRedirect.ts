import type { Router } from 'vue-router'

const fallbackRedirect = '/players'

export function resolveSafeRedirect(raw: unknown, router: Router): string {
  if (typeof raw !== 'string' || !raw.startsWith('/') || raw.startsWith('//'))
    return fallbackRedirect

  const target = router.resolve(raw)
  return target.matched.length > 0 && target.path !== '/login'
    ? target.fullPath
    : fallbackRedirect
}

import type { Pinia } from 'pinia'
import type { RouteRecordRaw, RouterHistory } from 'vue-router'
import { defineComponent, h, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { createRouter, createWebHistory } from 'vue-router'
import { routes } from 'vue-router/auto-routes'

import { useAuthStore } from '../features/auth'
import { resolveSafeRedirect } from '../features/auth/model/safeRedirect'
import { navigationRedirects } from './navigation/navigationRedirects'
import { canAccessRoute } from './navigation/routeAccess'

const forbiddenRoute: RouteRecordRaw = {
  path: '/forbidden',
  component: defineComponent({
    name: 'ForbiddenPage',
    setup() {
      const { t } = useI18n()
      return () => h('main', {
        'class': 'flex min-h-0 flex-1 items-center justify-center p-6 text-center',
        'data-testid': 'forbidden-page',
      }, [
        h('div', { class: 'max-w-md' }, [
          h('div', { class: 'text-4xl font-semibold text-warning' }, '403'),
          h('h1', { class: 'mt-3 text-lg font-semibold text-highlighted' }, t('forbidden.title')),
          h('p', { class: 'mt-2 text-sm text-muted' }, t('forbidden.description')),
        ]),
      ])
    },
  }),
  meta: { requiresAuth: true },
}

export function createAdminRouter(pinia: Pinia, history: RouterHistory = createWebHistory()) {
  const router = createRouter({ routes: [...navigationRedirects, ...routes, forbiddenRoute], history })
  const auth = useAuthStore(pinia)

  router.beforeEach((to) => {
    if (to.path === '/login' && auth.isAuthenticated)
      return resolveSafeRedirect(to.query.redirect, router)

    if (to.meta.requiresAuth && !auth.isAuthenticated) {
      return {
        path: '/login',
        query: { redirect: to.fullPath },
      }
    }

    if (!canAccessRoute(to.meta, auth.role, auth.isAuthenticated)) {
      return {
        path: '/forbidden',
        query: { from: to.fullPath },
      }
    }
  })

  watch(() => auth.isAuthenticated, (isAuthenticated) => {
    const currentRoute = router.currentRoute.value
    if (!isAuthenticated && currentRoute.meta.requiresAuth) {
      void router.replace({
        path: '/login',
        query: { redirect: currentRoute.fullPath },
      })
    }
  })

  return router
}

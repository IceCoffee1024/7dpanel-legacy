import type { ComputedRef, Ref } from 'vue'
import type {
  NavigationAccessContext,
  NavigationBreadcrumb,
  NavigationCatalog,
  NavigationEntryProjection,
  NavigationGroupProjection,
  NavigationRouteAdapter,
  NavigationRouteName,
  NavigationRouteSnapshot,
  NavigationShortcut,
} from './navigationTypes'
import { computed } from 'vue'

import { useRoute, useRouter } from 'vue-router'
import { useAuthStore } from '../../features/auth'
import { navigationCatalog } from './navigationCatalog'
import { canAccessRoute, createRouterRouteAdapter } from './routeAccess'

export interface UseNavigationOptions {
  readonly catalog?: NavigationCatalog
  readonly route?: Ref<NavigationRouteSnapshot>
  readonly access?: Ref<NavigationAccessContext>
  readonly routeAdapter?: NavigationRouteAdapter
}

function findEntry(catalog: NavigationCatalog, routeName: NavigationRouteName) {
  for (const group of catalog.groups) {
    const entry = group.children.find(child => child.routeName === routeName)
    if (entry !== undefined)
      return { group, entry }
  }
}

function routeChain(catalog: NavigationCatalog, routeName: NavigationRouteName): readonly NavigationRouteName[] {
  const chain = [routeName]
  let current = routeName
  let parent = catalog.routeParents.find(candidate => candidate.routeName === current)
  while (parent !== undefined) {
    chain.unshift(parent.parentRouteName)
    current = parent.parentRouteName
    parent = catalog.routeParents.find(candidate => candidate.routeName === current)
  }
  return chain
}

function getBreadcrumbLabel(catalog: NavigationCatalog, routeName: NavigationRouteName) {
  const entry = findEntry(catalog, routeName)?.entry
  if (entry !== undefined)
    return entry.labelKey
  return catalog.routeParents.find(parent => parent.routeName === routeName)?.labelKey
}

export function useNavigation(options: UseNavigationOptions = {}) {
  const router = options.routeAdapter === undefined ? useRouter() : undefined
  const auth = options.access === undefined ? useAuthStore() : undefined
  const currentRoute = options.route === undefined ? useRoute() : undefined
  const catalog = options.catalog ?? navigationCatalog
  const route = options.route ?? computed<NavigationRouteSnapshot>(() => ({
    name: currentRoute!.name as NavigationRouteName | undefined,
  }))
  const access = options.access ?? computed<NavigationAccessContext>(() => ({
    role: auth?.role ?? null,
    isAuthenticated: auth?.isAuthenticated ?? false,
  }))
  const routeAdapter = options.routeAdapter ?? createRouterRouteAdapter(router!)

  function canAccess(routeName: NavigationRouteName) {
    const meta = routeAdapter.getMeta(routeName)
    return meta !== undefined && canAccessRoute(meta, access.value.role, access.value.isAuthenticated)
  }

  const accessibleEntries = computed(() => catalog.groups.map(group => ({
    group,
    children: group.children.filter(child => canAccess(child.routeName)),
  })))

  const groups = computed<readonly NavigationGroupProjection[]>(() => Object.freeze(accessibleEntries.value
    .map(({ group, children: accessibleChildren }) => {
      const children = Object.freeze(accessibleChildren
        .filter(child => child.primary !== false)
        .map(child => Object.freeze({ ...child, groupId: group.id })))
      return Object.freeze({
        id: group.id,
        labelKey: group.labelKey,
        icon: group.icon,
        children,
      })
    })
    .filter(group => group.children.length > 0)))

  const activeGroupId = computed(() => {
    const routeName = route.value.name
    if (routeName === undefined)
      return undefined
    const chain = routeChain(catalog, routeName)
    return chain.map(name => findEntry(catalog, name)?.group.id).find((groupId): groupId is NavigationGroupProjection['id'] => groupId !== undefined)
  })

  const currentGroup = computed(() => groups.value.find(group => group.id === activeGroupId.value))

  const searchItems = computed<readonly NavigationEntryProjection[]>(() => Object.freeze(accessibleEntries.value
    .flatMap(({ group, children }) => children
      .filter(entry => entry.searchable)
      .map(entry => Object.freeze({ ...entry, groupId: group.id })))))

  const sectionItems = computed<readonly NavigationEntryProjection[]>(() => {
    const routeName = route.value.name
    if (routeName === undefined)
      return []
    const sectionId = routeChain(catalog, routeName)
      .map(name => findEntry(catalog, name)?.entry.sectionId)
      .find((candidate): candidate is NonNullable<typeof candidate> => candidate !== undefined)
    if (sectionId === undefined)
      return []
    return Object.freeze(accessibleEntries.value
      .flatMap(({ group, children }) => children
        .filter(entry => entry.sectionId === sectionId)
        .map(entry => Object.freeze({ ...entry, groupId: group.id }))))
  })

  const breadcrumbs = computed<readonly NavigationBreadcrumb[]>(() => {
    const routeName = route.value.name
    if (routeName === undefined)
      return []
    const chain = routeChain(catalog, routeName)
    const groupId = chain.map(name => findEntry(catalog, name)?.group.id).find(group => group !== undefined)
    const group = groupId === undefined ? undefined : catalog.groups.find(candidate => candidate.id === groupId)
    const firstRouteLabel = getBreadcrumbLabel(catalog, chain[0]!)
    const items: NavigationBreadcrumb[] = group === undefined || group.labelKey === firstRouteLabel
      ? []
      : [{ routeName: chain[0]!, labelKey: group.labelKey }]
    for (const name of chain) {
      const labelKey = getBreadcrumbLabel(catalog, name)
      if (labelKey !== undefined)
        items.push({ routeName: name, labelKey })
    }
    return Object.freeze(items)
  })

  const shortcuts = computed<readonly NavigationShortcut[]>(() => accessibleEntries.value
    .flatMap(({ children }) => children)
    .flatMap(entry => entry.shortcut === undefined ? [] : [Object.freeze({ shortcut: entry.shortcut, routeName: entry.routeName })]))

  return {
    groups: groups as ComputedRef<readonly NavigationGroupProjection[]>,
    activeGroupId,
    currentGroup: currentGroup as ComputedRef<NavigationGroupProjection | undefined>,
    searchItems: searchItems as ComputedRef<readonly NavigationEntryProjection[]>,
    sectionItems: sectionItems as ComputedRef<readonly NavigationEntryProjection[]>,
    breadcrumbs: breadcrumbs as ComputedRef<readonly NavigationBreadcrumb[]>,
    shortcuts: shortcuts as ComputedRef<readonly NavigationShortcut[]>,
  }
}

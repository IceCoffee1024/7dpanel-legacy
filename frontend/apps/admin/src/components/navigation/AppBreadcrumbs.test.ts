import type { NavigationBreadcrumb } from '../../app/navigation/navigationTypes'

import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'

import AppBreadcrumbs from './AppBreadcrumbs.vue'

const items = [
  { routeName: '/', labelKey: 'overview.title' },
  { routeName: '/players/', labelKey: 'players.navigation' },
  { routeName: '/players/profile/[crossplatformId]', labelKey: 'players.profile.detail' },
] as const satisfies readonly NavigationBreadcrumb[]

describe('app breadcrumbs', () => {
  it('renders only ancestors as buttons and marks the current page as text', () => {
    const wrapper = mount(AppBreadcrumbs, { props: { items } })

    expect(wrapper.findAll('button')).toHaveLength(2)
    expect(wrapper.find('[aria-current="page"]').element.tagName).toBe('SPAN')
    expect(wrapper.find('[aria-current="page"]').text()).toContain('玩家详情')
  })

  it('emits the selected ancestor route', async () => {
    const wrapper = mount(AppBreadcrumbs, { props: { items } })

    await wrapper.findAll('button')[1]!.trigger('click')

    expect(wrapper.emitted('select')).toEqual([['/players/']])
  })
})

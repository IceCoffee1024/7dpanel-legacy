import { mount } from '@vue/test-utils'
import { expect, it } from 'vitest'

import PlayersSectionNavigation from './PlayersSectionNavigation.vue'

it('links online, history and the protected player map as peer views', () => {
  const wrapper = mount(PlayersSectionNavigation, {
    global: {
      stubs: {
        SectionTabs: {
          props: ['items'],
          template: '<nav><a v-for="item in items" :key="item.id" :href="String(item.routeName)">{{ item.labelKey }}</a></nav>',
        },
      },
    },
  })

  expect(wrapper.get('a[href="/players/"]').text()).toBe('players.navigation')
  expect(wrapper.get('a[href="/players/history/"]').text()).toBe('players.profile.navigation')
  expect(wrapper.get('a[href="/players/map"]').text()).toBe('players.map.navigation')
})

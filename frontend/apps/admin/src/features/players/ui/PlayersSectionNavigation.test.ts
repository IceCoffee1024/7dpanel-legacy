import { mount } from '@vue/test-utils'
import { expect, it } from 'vitest'

import PlayersSectionNavigation from './PlayersSectionNavigation.vue'

it('links online, history and the protected player map as peer views', () => {
  const wrapper = mount(PlayersSectionNavigation, {
    global: {
      stubs: {
        Button: { props: ['label', 'to'], template: '<a :href="to">{{ label }}</a>' },
      },
    },
  })

  expect(wrapper.get('a[href="/players"]').text()).toBe('在线')
  expect(wrapper.get('a[href="/players/history"]').text()).toBe('历史')
  expect(wrapper.get('a[href="/players/map"]').text()).toBe('地图')
})

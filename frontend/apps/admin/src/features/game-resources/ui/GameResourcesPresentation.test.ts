import type { GameResourceItem } from '../api/gameResources'
import ui from '@nuxt/ui/vue-plugin'
import { mount } from '@vue/test-utils'
import { createPinia } from 'pinia'

import { describe, expect, it } from 'vitest'
import GameResourcesList from './GameResourcesList.vue'
import GameResourcesTable from './GameResourcesTable.vue'

const items: readonly GameResourceItem[] = Object.freeze([
  Object.freeze({
    resourceId: 'resource-1',
    numericId: 1,
    internalName: 'resourceIron',
    localizedName: '铁',
    kind: 'item',
    visibility: 'public',
    maxStack: 6000,
    hasQuality: true,
    iconStatus: 'missing',
    iconTintHex: 'AABBCC',
  }),
  Object.freeze({
    resourceId: 'resource-2',
    numericId: 2,
    internalName: 'resourceFallback',
    localizedName: null,
    kind: 'block',
    visibility: 'hidden',
    maxStack: null,
    hasQuality: null,
    iconStatus: 'invalid',
    iconTintHex: null,
  }),
])

function mountPresentation(component: typeof GameResourcesTable | typeof GameResourcesList) {
  return mount(component, {
    props: { items },
    global: {
      plugins: [createPinia(), ui],
      stubs: {
        GameResourceIcon: {
          props: ['alt'],
          template: '<span data-testid="icon">{{ alt }}</span>',
        },
      },
    },
  })
}

describe.each([
  ['desktop table', GameResourcesTable],
  ['narrow list', GameResourcesList],
] as const)('game resource %s', (_name, component) => {
  it('shows the same read-only item facts with localized fallback and tint text', () => {
    const wrapper = mountPresentation(component)

    expect(wrapper.text()).toContain('铁')
    expect(wrapper.text()).toContain('resourceIron')
    expect(wrapper.text()).toContain('resourceFallback')
    expect(wrapper.text()).toContain('6000')
    expect(wrapper.text()).toContain('#AABBCC')
    expect(wrapper.find('[data-tint="AABBCC"]').attributes('style')).toContain('#AABBCC')
  })

  it('emits only the internal name from the copy action', async () => {
    const wrapper = mountPresentation(component)

    await wrapper.get('[data-testid="copy-resourceIron"]').trigger('click')

    expect(wrapper.emitted('copy')).toEqual([['resourceIron']])
    expect(wrapper.emitted('copy')?.flat()).not.toContain('铁')
  })
})

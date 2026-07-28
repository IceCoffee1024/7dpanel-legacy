import { mount } from '@vue/test-utils'
import { expect, it, vi } from 'vitest'

import ConsoleCommandBar from './ConsoleCommandBar.vue'

vi.mock('@nuxt/ui/composables', () => ({
  useToast: () => ({ add: vi.fn() }),
}))

vi.mock('vue-i18n', () => ({
  useI18n: () => ({ t: (key: string) => key }),
}))

it('gives the command input a stable accessible form identity', () => {
  const wrapper = mount(ConsoleCommandBar, {
    props: {
      input: '',
      suggestions: [],
      selectedSuggestionIndex: 0,
      suggestionsOpen: false,
      catalogUnavailable: false,
      isSubmitting: false,
    },
    global: {
      stubs: {
        UButton: true,
        UInput: {
          inheritAttrs: false,
          props: ['modelValue'],
          template: '<input v-bind="$attrs" :value="modelValue">',
        },
      },
    },
  })

  expect(wrapper.get('#console-command').attributes()).toMatchObject({
    'aria-label': 'console.command.placeholder',
    'name': 'console-command',
  })
})

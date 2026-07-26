import type { ConsoleCommandCatalog } from '../api/consoleCommands'

import { PiniaColada } from '@pinia/colada'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { describe, expect, it, vi } from 'vitest'
import { defineComponent } from 'vue'

import { useConsoleCommands } from './useConsoleCommands'

const catalog: ConsoleCommandCatalog = {
  capturedAtUtc: '2026-07-26T12:00:00Z',
  commands: [
    {
      name: 'version',
      aliases: ['ver'],
      description: 'Show the game version.',
      help: 'Displays the current game version.',
      permissionLevel: 0,
    },
    {
      name: 'teleportplayer',
      aliases: ['tele', 'tp'],
      description: null,
      help: 'teleportplayer <name> <x> <y> <z>',
      permissionLevel: 1000,
    },
    {
      name: 'teleport',
      aliases: [],
      description: 'Teleport yourself.',
      help: null,
      permissionLevel: null,
    },
  ],
}

function mountCommands(options: {
  execute?: (command: string, signal?: AbortSignal) => Promise<unknown>
  subscribe?: (listener: (event: { type: string }) => void) => () => void
} = {}) {
  let commands!: ReturnType<typeof useConsoleCommands>
  const fetchCatalog = vi.fn().mockResolvedValue(catalog)
  const execute = vi.fn(options.execute ?? (() => Promise.resolve({ command: 'version', output: ['private output'] })))
  const invalidateCatalog = vi.fn().mockResolvedValue(undefined)
  const subscribe = vi.fn(options.subscribe ?? (() => () => {}))
  const Host = defineComponent({
    setup() {
      commands = useConsoleCommands({
        auth: {
          authorizationHeader: 'Bearer owner',
          expireSession: vi.fn(),
        },
        executeCommand: execute,
        fetchCatalog,
        invalidateCatalog,
        subscribeServerEvents: subscribe,
      })
      return () => null
    },
  })
  const wrapper = mount(Host, {
    global: { plugins: [createPinia(), PiniaColada] },
  })
  return { commands: () => commands, execute, fetchCatalog, invalidateCatalog, subscribe, wrapper }
}

describe('useConsoleCommands', () => {
  it('loads the five-field catalog and matches names or aliases by case-insensitive prefix in catalog order', async () => {
    const mounted = mountCommands()
    await flushPromises()

    mounted.commands().setInput('  TE')

    expect(mounted.fetchCatalog).toHaveBeenCalledOnce()
    expect(mounted.commands().suggestions.value.map(command => command.name))
      .toEqual(['teleportplayer', 'teleport'])
    expect(mounted.commands().suggestions.value[0]).toEqual(catalog.commands[1])
  })

  it('uses arrows to select suggestions and Tab to replace only the first word', async () => {
    const mounted = mountCommands()
    await flushPromises()
    mounted.commands().setInput('  te Player One')

    mounted.commands().moveSuggestion(1)
    expect(mounted.commands().selectedSuggestionIndex.value).toBe(1)
    expect(mounted.commands().completeSuggestion()).toBe(true)
    expect(mounted.commands().input.value).toBe('  teleport Player One')

    mounted.commands().dismissSuggestions()
    expect(mounted.commands().suggestionsOpen.value).toBe(false)
  })

  it('submits any non-empty command, ignores independent output, and records only successful responses', async () => {
    const mounted = mountCommands()
    await flushPromises()
    mounted.commands().setInput('thirdparty.do anything')

    await mounted.commands().submit()

    expect(mounted.execute).toHaveBeenCalledWith('thirdparty.do anything', expect.any(AbortSignal))
    expect(mounted.commands().input.value).toBe('')
    expect(mounted.commands().history.value).toEqual(['thirdparty.do anything'])
    expect('output' in mounted.commands()).toBe(false)
  })

  it('keeps at most 50 history entries, suppresses only consecutive duplicates, and restores the draft', async () => {
    const mounted = mountCommands()
    await flushPromises()

    for (let index = 0; index < 52; index++) {
      mounted.commands().setInput(`command-${index}`)
      await mounted.commands().submit()
    }
    mounted.commands().setInput('command-51')
    await mounted.commands().submit()
    mounted.commands().setInput('command-50')
    await mounted.commands().submit()
    mounted.commands().setInput('draft command')

    mounted.commands().navigateHistory(-1)
    expect(mounted.commands().input.value).toBe('command-50')
    mounted.commands().navigateHistory(1)
    expect(mounted.commands().input.value).toBe('draft command')
    expect(mounted.commands().history.value).toHaveLength(50)
    expect(mounted.commands().history.value[mounted.commands().history.value.length - 2]).toBe('command-51')
    expect(mounted.commands().history.value[mounted.commands().history.value.length - 1]).toBe('command-50')
  })

  it('preserves input and exposes safe feedback when submission fails', async () => {
    const mounted = mountCommands({ execute: () => Promise.reject(new Error('private backend detail')) })
    await flushPromises()
    mounted.commands().setInput('saveworld')

    await mounted.commands().submit()

    expect(mounted.commands().input.value).toBe('saveworld')
    expect(mounted.commands().feedback.value).toEqual({ code: 'unknown' })
    expect(JSON.stringify(mounted.commands().feedback.value)).not.toContain('private backend detail')
  })

  it('invalidates only the catalog query after game-ready and unsubscribes on unmount', async () => {
    let listener!: (event: { type: string }) => void
    const unsubscribe = vi.fn()
    const mounted = mountCommands({
      subscribe: (nextListener) => {
        listener = nextListener
        return unsubscribe
      },
    })
    await flushPromises()

    listener({ type: 'welcome' })
    listener({ type: 'game-ready' })
    await flushPromises()

    expect(mounted.invalidateCatalog).toHaveBeenCalledOnce()
    expect(mounted.invalidateCatalog).toHaveBeenCalledWith({ exact: true })
    mounted.wrapper.unmount()
    expect(unsubscribe).toHaveBeenCalledOnce()
  })
})

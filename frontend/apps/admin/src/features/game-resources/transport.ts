import type { InjectionKey } from 'vue'
import type { LoadGameResources } from './api/gameResources'

import { GameResourcesRequestError } from './api/gameResources'

export const GAME_RESOURCES_LOADER_KEY: InjectionKey<LoadGameResources> = Symbol('game-resources-loader')

export const unavailableGameResourcesLoader: LoadGameResources = () => Promise.reject(
  new GameResourcesRequestError('Game resource transport is unavailable', {
    status: 503,
    problemCode: 'game-resource-catalog-unavailable',
  }),
)

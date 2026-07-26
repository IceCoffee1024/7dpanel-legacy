import { gameResourcesGet } from '../../../shared/api/generated/sdk.gen'
import { createGameResourcesLoader } from './gameResources'

export const generatedGameResourcesLoader = createGameResourcesLoader(
  (query, signal) => gameResourcesGet({ query, signal }),
)

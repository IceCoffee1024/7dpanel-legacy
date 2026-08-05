import { describe, expect, it } from 'vitest'

import { submissions } from '../worldTools.test-fixtures'
import {
  createInitialWorldOperationForm,
  createWorldOperationReview,
} from './worldOperationForm'

describe('world operation form mapping', () => {
  it.each(submissions.map(item => item.type))('builds a closed %s submission and complete confirmation summary', (type) => {
    const form = createInitialWorldOperationForm()
    Object.assign(form, {
      type,
      targetId: 'entity-2',
      ownerStableIdentity: 'owner-1',
      entityId: 2,
      onlineObservedAtUtc: '2026-07-26T10:00:00.000Z',
      entityTypeResourceId: 'zombie-template',
      observedX: 10,
      observedY: 20,
      observedZ: 30,
      destinationX: 40,
      destinationY: 50,
      destinationZ: 60,
      firstX: -5,
      firstY: 10,
      firstZ: -4,
      secondX: 5,
      secondY: 20,
      secondZ: 4,
      catalogVersion: 'catalog-4',
      blockInternalName: 'stone',
      rotation: 1,
      blockShape: 'Cube',
      prefabResourceId: 'prefab-1',
      prefabInstanceId: 'instance-1',
      quantity: 2,
      radius: 8,
      maximumCount: 5,
      entityCategory: 'Hostile',
      reloadResourceKind: 'Blocks',
      sourceOperationId: 'operation-source',
      changeSetId: 'changeset-1',
      currentRegionHash: 'sha256:abc',
      sourceChangeSetId: 'changeset-source',
      boundsEnabled: true,
      minimumX: -100,
      minimumZ: -90,
      maximumX: 100,
      maximumZ: 90,
    })
    const review = createWorldOperationReview(form, {
      sourceState: 'Success',
      worldId: 'world-1',
      worldVersion: 'world-v7',
      seed: null,
      width: 8192,
      height: 8192,
      gameVersion: '3.0.1-b4',
      mapResourceVersion: 'map-v3',
      availableExtent: null,
      observedAtUtc: '2026-07-26T10:00:00.000Z',
    })

    expect(review.submission.type).toBe(type)
    expect(review).toMatchObject({
      worldId: 'world-1',
      worldVersion: 'world-v7',
      mapResourceVersion: 'map-v3',
    })
    expect(review.target.length).toBeGreaterThan(0)
    expect(review.scope.length).toBeGreaterThan(0)
    expect(review.impact.length).toBeGreaterThan(0)
    expect(typeof review.reversible).toBe('boolean')
  })

  it('marks strong operations explicitly while leaving ordinary operations single-confirmation', () => {
    const summary = {
      sourceState: 'Success' as const,
      worldId: 'world-1',
      worldVersion: 'world-v7',
      mapResourceVersion: 'map-v3',
      seed: null,
      width: null,
      height: null,
      gameVersion: null,
      availableExtent: null,
      observedAtUtc: '2026-07-26T10:00:00.000Z',
    }
    const ordinary = createInitialWorldOperationForm()
    Object.assign(ordinary, { type: 'collectGarbage' })
    const ordinaryReview = createWorldOperationReview(ordinary, summary)
    expect(ordinaryReview.strongConfirmation).toBe(false)
    expect(ordinaryReview.submission.request).toMatchObject({ confirmed: true })
    expect(ordinaryReview.submission.request).not.toHaveProperty('strongConfirmed')

    const strong = createInitialWorldOperationForm()
    Object.assign(strong, { type: 'renderFullMap' })
    const strongReview = createWorldOperationReview(strong, summary)
    expect(strongReview.strongConfirmation).toBe(true)
    expect(strongReview.submission.request).toMatchObject({ confirmed: true, strongConfirmed: true })
  })
})

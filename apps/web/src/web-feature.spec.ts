import type { Component } from 'vue'
import { describe, expect, it } from 'vitest'
import {
  composeWebFeatures,
  WebFeatureCompositionError,
  type ExactContractVersion,
  type WebFeatureDescriptor
} from './web-feature'
import { platformShellFeature, webFeatureComposition, webFeatureRegistry } from './web-feature-registry'
import { receivingFeature } from './features/receiving/receiving-feature'
import { labWorkbenchFeature } from './features/lab-workbench/lab-workbench-feature'

const component = {} as Component

function feature(
  featureId: string,
  routeName: string,
  routePath: string,
  contractVersion: ExactContractVersion = '1.0.0'
): WebFeatureDescriptor {
  return {
    featureId,
    contractVersion,
    routes: [{ name: routeName, path: routePath, component }],
    navigationEntries: [{ id: `${featureId}.navigation`, label: featureId, routeName }]
  }
}

function expectCompositionError(
  action: () => unknown,
  code: WebFeatureCompositionError['code'],
  identifier: string
): void {
  try {
    action()
    expect.fail('Expected web feature composition to fail.')
  } catch (error) {
    expect(error).toBeInstanceOf(WebFeatureCompositionError)
    expect(error).toMatchObject({ code, identifier })
  }
}

describe('composeWebFeatures', () => {
  it('composes routes and navigation in explicit registry order', () => {
    const first = feature('FEATURE-A', 'feature-a.home', '/feature-a')
    const second = feature('FEATURE-B', 'feature-b.home', '/feature-b', '2.3.4')

    const composition = composeWebFeatures([first, second])

    expect(composition.features).toEqual([first, second])
    expect(composition.routes.map((route) => route.name)).toEqual(['feature-a.home', 'feature-b.home'])
    expect(composition.navigationEntries.map((entry) => entry.id)).toEqual([
      'FEATURE-A.navigation',
      'FEATURE-B.navigation'
    ])
  })

  it('accepts an empty registry without inventing routes or navigation', () => {
    const composition = composeWebFeatures([])

    expect(composition).toEqual({ features: [], routes: [], navigationEntries: [] })
  })

  it('rejects duplicate feature ids deterministically', () => {
    expectCompositionError(
      () => composeWebFeatures([
        feature('FEATURE-A', 'feature-a.home', '/feature-a'),
        feature('FEATURE-A', 'feature-a.other', '/feature-a-other')
      ]),
      'DUPLICATE_FEATURE_ID',
      'FEATURE-A'
    )
  })

  it('rejects duplicate route names deterministically', () => {
    expectCompositionError(
      () => composeWebFeatures([
        feature('FEATURE-A', 'shared.home', '/feature-a'),
        feature('FEATURE-B', 'shared.home', '/feature-b')
      ]),
      'DUPLICATE_ROUTE_NAME',
      'shared.home'
    )
  })

  it('rejects equivalent route paths with or without a trailing slash', () => {
    expectCompositionError(
      () => composeWebFeatures([
        feature('FEATURE-A', 'feature-a.home', '/shared'),
        feature('FEATURE-B', 'feature-b.home', '/shared/')
      ]),
      'DUPLICATE_ROUTE_PATH',
      '/shared/'
    )
  })

  it.each(['latest', '^1.0.0', '1.0', '01.0.0', '1.0.0-beta']) (
    'rejects non-exact contract version %s',
    (contractVersion) => {
      expectCompositionError(
        () => composeWebFeatures([
          feature('FEATURE-A', 'feature-a.home', '/feature-a', contractVersion as ExactContractVersion)
        ]),
        'INVALID_CONTRACT_VERSION',
        contractVersion
      )
    }
  )

  it('rejects relative routes and navigation that targets another feature', () => {
    expectCompositionError(
      () => composeWebFeatures([feature('FEATURE-A', 'feature-a.home', 'feature-a')]),
      'INVALID_ROUTE',
      'feature-a'
    )

    const descriptor = feature('FEATURE-A', 'feature-a.home', '/feature-a')
    expectCompositionError(
      () => composeWebFeatures([{ ...descriptor, navigationEntries: [
        { id: 'foreign', label: 'Foreign', routeName: 'feature-b.home' }
      ] }]),
      'INVALID_NAVIGATION_ENTRY',
      'foreign'
    )
  })
})

describe('production web feature registry', () => {
  it('contains the platform shell and all explicitly approved production features', () => {
    expect(webFeatureRegistry).toEqual([platformShellFeature, receivingFeature, labWorkbenchFeature])
    expect(webFeatureComposition.routes.map((route) => [route.name, route.path])).toEqual([
      ['platform.home', '/'],
      ['platform.system-status', '/system/status'],
      ['platform.auth-callback', '/auth/callback'],
      ['receiving.registration', '/receiving/receipts/new'],
      ['workbench.scope', '/workbench/scope'],
      ['workbench.quantity', '/workbench/quantity'],
      ['workbench.allocation', '/workbench/allocation'],
      ['workbench.batch', '/workbench/batch']
    ])
    expect(webFeatureComposition.navigationEntries).toEqual([
      { id: 'platform.system-status', label: 'System status', routeName: 'platform.system-status' },
      { id: 'receiving.registration', label: '到货登记', routeName: 'receiving.registration' },
      { id: 'workbench.scope', label: '范围矩阵', routeName: 'workbench.scope' },
      { id: 'workbench.quantity', label: '数量账', routeName: 'workbench.quantity' },
      { id: 'workbench.allocation', label: '样品分配', routeName: 'workbench.allocation' },
      { id: 'workbench.batch', label: '批次管理', routeName: 'workbench.batch' }
    ])
  })
})

import type { RouteRecordRaw } from 'vue-router'

export type ExactContractVersion = `${number}.${number}.${number}`

export type WebFeatureRoute = RouteRecordRaw & {
  readonly name: string
  readonly path: string
}

export interface WebNavigationEntry {
  readonly id: string
  readonly label: string
  readonly routeName: string
}

export interface WebFeatureDescriptor {
  readonly featureId: string
  readonly contractVersion: ExactContractVersion
  readonly routes: readonly WebFeatureRoute[]
  readonly navigationEntries: readonly WebNavigationEntry[]
}

export interface WebFeatureComposition {
  readonly features: readonly WebFeatureDescriptor[]
  readonly routes: readonly WebFeatureRoute[]
  readonly navigationEntries: readonly WebNavigationEntry[]
}

export type WebFeatureCompositionErrorCode =
  | 'DUPLICATE_FEATURE_ID'
  | 'DUPLICATE_ROUTE_NAME'
  | 'DUPLICATE_ROUTE_PATH'
  | 'INVALID_CONTRACT_VERSION'
  | 'INVALID_FEATURE_ID'
  | 'INVALID_NAVIGATION_ENTRY'
  | 'INVALID_ROUTE'

export class WebFeatureCompositionError extends Error {
  constructor(
    public readonly code: WebFeatureCompositionErrorCode,
    public readonly identifier: string,
    message: string
  ) {
    super(message)
    this.name = 'WebFeatureCompositionError'
  }
}

const exactContractVersionPattern = /^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$/

export function composeWebFeatures(
  descriptors: readonly WebFeatureDescriptor[]
): WebFeatureComposition {
  const features: WebFeatureDescriptor[] = []
  const routes: WebFeatureRoute[] = []
  const navigationEntries: WebNavigationEntry[] = []
  const featureIds = new Set<string>()
  const routeNames = new Map<string, string>()
  const routePaths = new Map<string, string>()

  for (const descriptor of descriptors) {
    assertFeatureDescriptor(descriptor)
    if (featureIds.has(descriptor.featureId)) {
      throw new WebFeatureCompositionError(
        'DUPLICATE_FEATURE_ID',
        descriptor.featureId,
        `Duplicate web feature id "${descriptor.featureId}".`
      )
    }

    featureIds.add(descriptor.featureId)
    features.push(descriptor)

    const featureRouteNames = new Set<string>()
    for (const route of descriptor.routes) {
      assertRoute(route, descriptor.featureId)
      const previousNameOwner = routeNames.get(route.name)
      if (previousNameOwner) {
        throw new WebFeatureCompositionError(
          'DUPLICATE_ROUTE_NAME',
          route.name,
          `Duplicate route name "${route.name}" in web features "${previousNameOwner}" and "${descriptor.featureId}".`
        )
      }

      const canonicalPath = canonicalRoutePath(route.path)
      const previousPathOwner = routePaths.get(canonicalPath)
      if (previousPathOwner) {
        throw new WebFeatureCompositionError(
          'DUPLICATE_ROUTE_PATH',
          route.path,
          `Duplicate route path "${route.path}" in web features "${previousPathOwner}" and "${descriptor.featureId}".`
        )
      }

      routeNames.set(route.name, descriptor.featureId)
      routePaths.set(canonicalPath, descriptor.featureId)
      featureRouteNames.add(route.name)
      routes.push(route)
    }

    for (const entry of descriptor.navigationEntries) {
      assertNavigationEntry(entry, descriptor.featureId, featureRouteNames)
      navigationEntries.push(entry)
    }
  }

  return Object.freeze({
    features: Object.freeze(features),
    routes: Object.freeze(routes),
    navigationEntries: Object.freeze(navigationEntries)
  })
}

function assertFeatureDescriptor(descriptor: WebFeatureDescriptor): void {
  if (!descriptor.featureId.trim()) {
    throw new WebFeatureCompositionError(
      'INVALID_FEATURE_ID',
      descriptor.featureId,
      'Web feature id must not be empty.'
    )
  }
  if (!exactContractVersionPattern.test(descriptor.contractVersion)) {
    throw new WebFeatureCompositionError(
      'INVALID_CONTRACT_VERSION',
      descriptor.contractVersion,
      `Web feature "${descriptor.featureId}" must declare an exact major.minor.patch contract version.`
    )
  }
}

function assertRoute(route: WebFeatureRoute, featureId: string): void {
  if (!route.name.trim()) {
    throw new WebFeatureCompositionError(
      'INVALID_ROUTE',
      route.name,
      `Web feature "${featureId}" contains a route with an empty name.`
    )
  }
  if (!route.path.startsWith('/')) {
    throw new WebFeatureCompositionError(
      'INVALID_ROUTE',
      route.path,
      `Route "${route.name}" in web feature "${featureId}" must use an absolute path.`
    )
  }
}

function assertNavigationEntry(
  entry: WebNavigationEntry,
  featureId: string,
  featureRouteNames: ReadonlySet<string>
): void {
  if (!entry.id.trim() || !entry.label.trim() || !entry.routeName.trim()) {
    throw new WebFeatureCompositionError(
      'INVALID_NAVIGATION_ENTRY',
      entry.id,
      `Web feature "${featureId}" contains an incomplete navigation entry.`
    )
  }
  if (!featureRouteNames.has(entry.routeName)) {
    throw new WebFeatureCompositionError(
      'INVALID_NAVIGATION_ENTRY',
      entry.id,
      `Navigation entry "${entry.id}" in web feature "${featureId}" must target a route owned by that feature.`
    )
  }
}

function canonicalRoutePath(path: string): string {
  return path.length > 1 ? path.replace(/\/+$/, '') : path
}

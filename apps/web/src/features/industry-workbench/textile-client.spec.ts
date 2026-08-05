import { describe, expect, it, vi } from 'vitest'
import {
  approveTextileCuttingPlan,
  calculateTextileSampleRequirement,
  createTextileCuttingPlan,
  getTextileCuttingPlan,
  TEXTILE_RULE_SET_VERSION,
  type CreateTextileCuttingPlanRequest,
  type CreateTextileSampleRequirementRequest
} from './textile-client'

const ref = (id: string) => ({ id, version: 1 })
const context = { accessToken: 'token', correlationId: 'corr' }

describe('Textile typed client', () => {
  it('covers all four runtime operations with exact paths, versions, and no trusted context', async () => {
    const fetcher = successFetcher()
    await calculateTextileSampleRequirement(requirementRequest(), { ...context, fetcher })
    await createTextileCuttingPlan(planRequest(), { ...context, fetcher })
    await approveTextileCuttingPlan('plan/1', 2, {
      expectedCurrentVersion: 2,
      sampleRequirementInputHash: 'requirement-hash',
      ruleSetVersion: TEXTILE_RULE_SET_VERSION,
      approvalComment: 'reviewed'
    }, { ...context, fetcher })
    await getTextileCuttingPlan('plan/1', 2, { ...context, fetcher })

    expect(paths(fetcher)).toEqual([
      '/api/v1/textile/sample-requirements',
      '/api/v1/textile/cutting-plans',
      '/api/v1/textile/cutting-plans/plan%2F1/versions/2/approval',
      '/api/v1/textile/cutting-plans/plan%2F1/versions/2'
    ])
    expect(methods(fetcher)).toEqual(['POST', 'POST', 'POST', 'GET'])
    expect(requestBodies(fetcher).join('')).not.toContain('organizationGroupId')
    expect(requestBodies(fetcher).join('')).not.toContain('actorId')
    expect(requestBodies(fetcher).join('')).not.toContain('approvedBy')
  })
})

function requirementRequest(): CreateTextileSampleRequirementRequest {
  return {
    requirementId: 'req-1', expectedCurrentVersion: 0,
    objectScope: { legalEntityId: 'legal', laboratoryId: 'lab' },
    calculation: {
      ruleSetVersion: TEXTILE_RULE_SET_VERSION,
      demandLines: [{
        style: ref('style'), colorway: ref('red'), component: ref('front'),
        material: ref('cotton'), position: 'body', direction: 'WARP', testItem: ref('tensile'),
        parallelCount: 3, retestReserveCount: 1, retentionReserveCount: 1,
        destructive: true, specimenLengthMm: 10, specimenWidthMm: 12,
        exclusiveDestructiveGroupId: 'group-a'
      }],
      availableFabrics: [{
        style: ref('style'), colorway: ref('red'), component: ref('front'),
        position: 'body', availableAreaSquareMm: 1000
      }]
    }
  }
}

function planRequest(): CreateTextileCuttingPlanRequest {
  return {
    cuttingPlanId: 'plan-1', expectedCurrentVersion: 0,
    sampleRequirementId: 'req-1', sampleRequirementVersion: 1,
    sampleRequirementInputHash: 'requirement-hash', ruleSetVersion: TEXTILE_RULE_SET_VERSION,
    plan: {
      cuttingPlanId: 'plan-1', sourceItem: ref('fabric'), samplingPosition: 'body',
      direction: 'WARP', lengthMm: 10, widthMm: 12, plannedCount: 1,
      minDistanceFromSelvedgeMm: 20, templateVersion: 'template@1.0.0',
      operatorId: 'operator', generatedSpecimenIds: ['spec-1']
    }
  }
}

function successFetcher() {
  return vi.fn(async () => new Response('{}', {
    status: 200, headers: { 'Content-Type': 'application/json' }
  })) as unknown as typeof fetch & { mock: { calls: [string, RequestInit][] } }
}

function paths(fetcher: ReturnType<typeof successFetcher>): string[] {
  return fetcher.mock.calls.map(call => call[0])
}

function methods(fetcher: ReturnType<typeof successFetcher>): string[] {
  return fetcher.mock.calls.map(call => String(call[1]?.method ?? 'GET'))
}

function requestBodies(fetcher: ReturnType<typeof successFetcher>): string[] {
  return fetcher.mock.calls.map(call => String(call[1]?.body ?? ''))
}

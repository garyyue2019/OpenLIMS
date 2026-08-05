import { describe, expect, it, vi } from 'vitest'
import {
  freezeToyAgeGradeDecision, getToyProductOverview, recordToyAccessibilityAssessment,
  recordToyAgeDeclaration, recordToyAgeGradeDecision, resolveToyReassessmentTrigger,
  TOY_PRODUCT_RULE_SET_VERSION
} from './toy-product-client'
import {
  approveToySampleRequirement, createToyTestUnitPlan, getToyTestUnitPlan,
  requestToyAllocation, TOY_TEST_UNIT_RULE_SET_VERSION
} from './toy-test-unit-client'
import {
  appendToyLabelArtifactVersion, createToyLabelArtifact, createToyLabelReview,
  decideToyLabelReview, getToyLabelReviewStatus, TOY_LABEL_REVIEW_RULE_SET_VERSION
} from './toy-label-review-client'
import {
  createToyItemConclusion, createToyScopeConclusion, getToyConclusion,
  getToyConclusionsByProduct, TOY_CONCLUSION_RULE_SET_VERSION
} from './toy-conclusion-client'

const context = { accessToken: 'token', correlationId: 'corr' }
const scope = { legalEntityId: 'legal', laboratoryId: 'lab' }
const ref = (id: string) => ({ id, version: 1 })
const hash = 'a'.repeat(64)

describe('Toy typed clients', () => {
  it('covers all 19 runtime operations with exact paths, methods, and no trusted context', async () => {
    const fetcher = successFetcher()
    const ctx = { ...context, fetcher }

    await recordToyAgeDeclaration('product/1', {
      ruleSetVersion: TOY_PRODUCT_RULE_SET_VERSION, objectScope: scope, expectedCurrentVersion: 0,
      declaredMinimumAgeMonths: 36, intendedUse: 'play', declarationSource: 'customer'
    }, ctx)
    await recordToyAgeGradeDecision('product/1', {
      ruleSetVersion: TOY_PRODUCT_RULE_SET_VERSION, objectScope: scope, expectedCurrentVersion: 0,
      minimumAgeMonths: 36, rationale: 'evidence', standardRef: ref('standard'), approvedBy: 'business-ref'
    }, ctx)
    await freezeToyAgeGradeDecision('product/1', 2, {
      ruleSetVersion: TOY_PRODUCT_RULE_SET_VERSION, expectedCurrentVersion: 1
    }, ctx)
    await recordToyAccessibilityAssessment('product/1', {
      ruleSetVersion: TOY_PRODUCT_RULE_SET_VERSION, objectScope: scope, expectedCurrentVersion: 0,
      stage: 'INITIAL', accessibleParts: ['wheel']
    }, ctx)
    await resolveToyReassessmentTrigger('product/1', 'trigger/1', {
      ruleSetVersion: TOY_PRODUCT_RULE_SET_VERSION, expectedCurrentVersion: 1,
      resolutionRef: ref('resolution')
    }, ctx)
    await getToyProductOverview('product/1', ctx)

    await createToyTestUnitPlan('product/1', {
      ruleSetVersion: TOY_TEST_UNIT_RULE_SET_VERSION, objectScope: scope, expectedCurrentVersion: 0,
      productVersion: 1, ageGradeDecisionVersion: 1, accessibilityAssessmentVersion: 1,
      scopeMatrixId: 'matrix', scopeMatrixVersion: 1, scopeLineRefs: [ref('line')],
      sampleRuleRefs: [ref('rule')], testUnits: [{
        testUnitId: 'TU-1', physicalObjectRef: ref('item'), hazardDomainRefs: [ref('MECH')],
        parallelNumber: 1, sequenceSteps: [{ stepId: 'S-1', sequenceOrder: 1, taskRef: ref('task'), destructive: true }]
      }], demandInputs: [{
        componentId: 'base', kind: 'BASE', amount: 1, dimension: 'COUNT', unit: 'piece',
        sourceRuleRef: ref('rule'), applicability: 'ALLOWED'
      }]
    }, ctx)
    await approveToySampleRequirement('product/1', 2, {
      expectedCurrentVersion: 2, ruleSetVersion: TOY_TEST_UNIT_RULE_SET_VERSION,
      inputHash: 'server-hash', approvalComment: 'approved'
    }, ctx)
    await requestToyAllocation('product/1', 2, {
      expectedCurrentVersion: 3, ruleSetVersion: TOY_TEST_UNIT_RULE_SET_VERSION,
      quantityChecks: [{
        quantityAccountId: 'account', expectedAccountVersion: 1, ruleSetVersion: 'QUANTITY@1.0.0',
        amount: 1, dimension: 'COUNT', unit: 'piece', reservationRef: 'reservation'
      }], allocationChecks: [{
        allocationId: 'allocation', expectedSubjectAllocationVersion: 1,
        ruleSetVersion: 'ALLOCATION@1.0.0', testUnitId: 'TU-1', sequenceStepId: 'S-1'
      }]
    }, ctx)
    await getToyTestUnitPlan('product/1', 2, ctx)

    const imageEvidenceRefs = [{ objectRef: { bucket: 'evidence', objectKey: 'label.png' }, hash }]
    await createToyLabelArtifact('product/1', {
      objectScope: scope, expectedCurrentVersion: 0, artifactType: 'LABEL', language: 'zh-CN',
      market: 'CN', contentHash: hash, imageEvidenceRefs
    }, ctx)
    await appendToyLabelArtifactVersion('product/1', 'artifact/1', {
      expectedCurrentVersion: 1, contentHash: hash, imageEvidenceRefs
    }, ctx)
    await createToyLabelReview('product/1', 'artifact/1', {
      expectedCurrentVersion: 0, artifactVersion: 1, productVersion: 1,
      ageGradeDecisionVersion: 1, market: 'CN', language: 'zh-CN', reviewScopeRefs: [ref('scope')],
      impactRuleRef: ref('impact-rule'), ruleSetVersion: TOY_LABEL_REVIEW_RULE_SET_VERSION
    }, ctx)
    await decideToyLabelReview('product/1', 'review/1', {
      expectedCurrentVersion: 1, decision: 'APPROVED', decisionReason: 'reviewed'
    }, ctx)
    await getToyLabelReviewStatus('product/1', {
      productVersion: 2, ageGradeDecisionVersion: 3, market: 'CN', language: 'zh-CN', artifactType: 'LABEL'
    }, ctx)

    await createToyItemConclusion({
      ruleSetVersion: TOY_CONCLUSION_RULE_SET_VERSION, adoptedResultRef: 'result', adoptedResultVersion: 1,
      requirementRef: 'requirement', requirementVersion: 1
    }, ctx)
    await createToyScopeConclusion({
      ruleSetVersion: TOY_CONCLUSION_RULE_SET_VERSION, productRef: 'product/1', productVersion: 2,
      testUnitPlanRef: 'plan', testUnitPlanVersion: 3, testUnits: [{
        testUnitId: 'TU-1', physicalObjectRef: 'item', physicalObjectVersion: 1,
        hazardDomainRef: 'MECH', hazardDomainVersion: 1, adoptedResultRef: 'result', adoptedResultVersion: 1,
        resultProvenanceGraphRef: 'graph', resultProvenanceGraphVersion: 1,
        coverageDecisionRef: 'coverage', coverageDecisionVersion: 1, requirementRefs: ['requirement']
      }], uncoveredScopes: [{ scope: 'CHEM', reason: 'NOT_TESTED', detail: 'outside scope' }],
      externalReferences: [{ issuer: 'customer', reference: 'ext', statedScope: 'declared', notPartOfThisConclusion: true }],
      isFictitiousWholeItemConclusion: false, reauthenticationRef: ref('reauth'),
      signingIntent: 'approve', signedContentHash: hash
    }, ctx)
    await getToyConclusion('conclusion/1', ctx)
    await getToyConclusionsByProduct('product/1', 2, ctx)

    expect(paths(fetcher)).toEqual([
      '/api/v1/toy/products/product%2F1/age-declarations',
      '/api/v1/toy/products/product%2F1/age-grade-decisions',
      '/api/v1/toy/products/product%2F1/age-grade-decisions/2/freeze',
      '/api/v1/toy/products/product%2F1/accessibility-assessments',
      '/api/v1/toy/products/product%2F1/reassessment-triggers/trigger%2F1/resolution',
      '/api/v1/toy/products/product%2F1/overview',
      '/api/v1/toy/products/product%2F1/test-unit-plans',
      '/api/v1/toy/products/product%2F1/test-unit-plans/2/approval',
      '/api/v1/toy/products/product%2F1/test-unit-plans/2/allocations',
      '/api/v1/toy/products/product%2F1/test-unit-plans/2',
      '/api/v1/toy/products/product%2F1/label-artifacts',
      '/api/v1/toy/products/product%2F1/label-artifacts/artifact%2F1/versions',
      '/api/v1/toy/products/product%2F1/label-artifacts/artifact%2F1/reviews',
      '/api/v1/toy/products/product%2F1/label-reviews/review%2F1/decision',
      `/api/v1/toy/products/product%2F1/label-reviews/status?productVersion=2&ageGradeDecisionVersion=3&market=CN&language=zh-CN&artifactType=LABEL&ruleSetVersion=${encodeURIComponent(TOY_LABEL_REVIEW_RULE_SET_VERSION)}`,
      '/api/v1/toy/conclusions/item-conformity',
      '/api/v1/toy/conclusions/tested-scope-conformity',
      '/api/v1/toy/conclusions/conclusion%2F1',
      '/api/v1/toy/conclusions?productRef=product%2F1&productVersion=2'
    ])
    expect(methods(fetcher)).toEqual([
      'POST', 'POST', 'POST', 'POST', 'POST', 'GET', 'POST', 'POST', 'POST', 'GET',
      'POST', 'POST', 'POST', 'POST', 'GET', 'POST', 'POST', 'GET', 'GET'
    ])
    const bodies = requestBodies(fetcher).join('')
    expect(bodies).not.toContain('organizationGroupId')
    expect(bodies).not.toContain('actorId')
    expect(bodies).not.toContain('reviewedBy')
    expect(bodies).not.toContain('createdBy')
  })
})

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

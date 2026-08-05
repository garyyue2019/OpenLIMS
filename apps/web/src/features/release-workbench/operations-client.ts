import { labRequest, type LabClientContext } from '../lab-workbench/lab-api'

export const OPERATIONS_RULE_SET_VERSION = 'OPERATIONS@1.0.0'

export interface OperationsVersionedReference { id: string; version: number }
export interface OperationsObjectScope {
  legalEntityId: string
  laboratoryId: string
  customerId: string
  serviceOrderId: string
  productCategory: string
}
export interface CreateLineageEdgeRequest {
  sourceObjectId: string
  targetObjectId: string
  relationKind: string
  basis: OperationsVersionedReference
  objectScope: OperationsObjectScope
}
export interface RecordCustodyEventRequest {
  objectId: string
  eventKind: string
  fromLocationId?: string
  toLocationId: string
  responsiblePartyId: string
  evidenceRef: string
  objectScope: OperationsObjectScope
}
export interface WorkTaskInput {
  taskId: string
  scopeLineId: string
  method: OperationsVersionedReference
  workCenterId: string
  priority: number
  sequence: number
  destructive: boolean
  plannedStart?: string
  plannedEnd?: string
  dependencyTaskIds: string[]
}
export interface CreateWorkPlanRequest {
  scopeMatrix: OperationsVersionedReference
  sampleIdentity: OperationsVersionedReference
  tasks: WorkTaskInput[]
  objectScope: OperationsObjectScope
}
export interface ChangeWorkTaskStateRequest {
  expectedPlanVersion: number
  state: string
  reason: string
}
export interface ReserveResourceRequest {
  expectedPlanVersion: number
  taskId: string
  resourceKind: string
  resourceId: string
  startsAt: string
  endsAt: string
}
export interface WorkPlanResult {
  workPlanId: string
  version: number
  ruleSetVersion: string
  state: string
  objectScope: OperationsObjectScope
  tasks: unknown[]
  reservations: unknown[]
}
export interface LineageGraphResult { objectId: string; ruleSetVersion: string; edges: unknown[] }
export interface CustodyChainResult { objectId: string; ruleSetVersion: string; events: unknown[] }
export interface WorkQueueResult {
  workCenterId: string
  state?: string
  ruleSetVersion: string
  items: unknown[]
}

export function createLineageEdge(
  request: CreateLineageEdgeRequest,
  context: LabClientContext
): Promise<unknown> {
  return labRequest('/api/v1/sample-lineage/edges', { ...context, method: 'POST', body: request })
}

export function getSampleLineage(
  objectId: string,
  context: LabClientContext
): Promise<LineageGraphResult> {
  return labRequest(`/api/v1/sample-lineage/${encodeURIComponent(objectId)}`, context)
}

export function recordCustodyEvent(
  request: RecordCustodyEventRequest,
  context: LabClientContext
): Promise<unknown> {
  return labRequest('/api/v1/custody-events', { ...context, method: 'POST', body: request })
}

export function getCustodyChain(
  objectId: string,
  context: LabClientContext
): Promise<CustodyChainResult> {
  return labRequest(`/api/v1/samples/${encodeURIComponent(objectId)}/custody`, context)
}

export function createWorkPlan(
  request: CreateWorkPlanRequest,
  context: LabClientContext
): Promise<WorkPlanResult> {
  return labRequest('/api/v1/work-plans', { ...context, method: 'POST', body: request })
}

export function getWorkPlan(workPlanId: string, context: LabClientContext): Promise<WorkPlanResult> {
  return labRequest(`/api/v1/work-plans/${encodeURIComponent(workPlanId)}`, context)
}

export function changeWorkTaskState(
  workPlanId: string,
  taskId: string,
  request: ChangeWorkTaskStateRequest,
  context: LabClientContext
): Promise<WorkPlanResult> {
  return labRequest(
    `/api/v1/work-plans/${encodeURIComponent(workPlanId)}/tasks/${encodeURIComponent(taskId)}/state`,
    { ...context, method: 'POST', body: request }
  )
}

export function reserveWorkResource(
  workPlanId: string,
  request: ReserveResourceRequest,
  context: LabClientContext
): Promise<WorkPlanResult> {
  return labRequest(`/api/v1/work-plans/${encodeURIComponent(workPlanId)}/resource-reservations`, {
    ...context, method: 'POST', body: request
  })
}

export function getWorkQueue(
  workCenterId: string,
  state: string | undefined,
  context: LabClientContext
): Promise<WorkQueueResult> {
  const query = new URLSearchParams({ workCenterId })
  if (state) query.set('state', state)
  return labRequest(`/api/v1/work-queues?${query}`, context)
}

<!-- GENERATED FILE — DO NOT EDIT.
Generator: openlims-specgen@0.1.0
Sources: ATC-ALLOC-001@1.0.0
Edit files under spec/ and run `python -m tools.specgen generate`.
-->

# ATC-ALLOC-001：实施 DEV-010 任务分配资格

## 元数据

| 字段 | 值 |
|---|---|
| 规格版本 | `1.0.0` |
| 评审状态 | `approved` |
| 目标发布 | `REL-R1-RECEIVING-PILOT@1.0.0` |
| Epic | `EP-EXECUTION` |
| Feature | `FEAT-TASK-ALLOCATION` |
| 开发就绪度 | `ready` |
| 变更级别 | `major` |
| 负责人角色 | 实验室技术负责人, 技术负责人, 质量负责人, QA负责人 |
| 影响模块 | allocation, receiving, scope, quantity, eligibility-gate, authorization, audit, outbox, automated-test |
| 来源 | PRD-MAIN#OPS-ALLOC-001, PRD-MAIN#OPS-ALLOC-002, PRD-MAIN#OPS-ALLOC-003, PRD-MAIN#OPS-ALLOC-004, PRD-MAIN#AC-ELEC-003 |
| 固定依赖 | ATC-PLT-003@1.0.0, ED-001@2.0.0, OD-002@1.0.0, OD-009@1.0.0, OD-010@1.0.0, OD-027@1.0.0, OD-035@1.0.0, ATC-REC-006@2.0.0, ATC-SCP-001@1.0.0, ATC-QTY-001@1.0.0, BUS-ALLOC-001@1.0.0, BUS-ALLOC-002@1.0.0, BUS-ALLOC-003@1.0.0, AC-ELEC-003@1.0.0, SEC-AUTH-001@1.0.0, SEC-AUD-001@2.0.0, NFR-ARCH-001@2.0.0 |
| 规格指纹 | `d8e63c13c3459ba2c9811cdfaa36fa55b2741d4fc49af2bc0bf746678e525ca1` |

## 业务结果

实验室在创建任何任务使用前，必须获得身份/隔离、范围资格和数量可用性三重版本固定许可；破坏性使用互斥、并发超分配和一切未知语义被系统性阻断，下游可用公共端口验证分配的精确版本状态。

## 主要参与者

具有 allocation.assign 及法人、实验室、客户、委托和产品类别对象范围的授权计划人

## 触发条件

授权计划人为物理对象向范围行和计划/序列步骤创建 TestObjectAllocation 或释放既有分配

## 前置条件

- DEV-002 模块接入通道已交付
- DEV-005/007 Receiving 资格端口 v2、DEV-008 Scope 资格端口、DEV-009 Quantity 可用量端口均已交付
- 部署绑定唯一 OrganizationGroup
- 调用身份由服务端建立
- 对象引用由调用方提交精确稳定 ID 和版本

## 正常路径

- 校验 actor capability 和对象范围
- 校验请求引用完整性、validUntil 未过期和金额精度
- 在独立事务依次评估 Receiving 资格（action=TEST_ASSIGNMENT，语义即 OD-035 的 TEST_OBJECT_ALLOCATION）、Scope 生产资格和 Quantity 可用量端口且全部 ALLOWED
- 开启自身事务：advisory lock 物理对象、校验 expectedCurrentVersion、校验无活跃破坏性分配
- 原子保存不可变分配事实（固定三端口决定、对象版本和规则版本）、审计和 Outbox
- 公共 AllocationStatusPort 对当前分配版本返回 ALLOWED

## 失败路径

- 缺失必需引用、金额精度或 validUntil 非法时返回 ALC.VALIDATION_FAILED
- validUntil 已过期返回 ALC.ALLOCATION_EXPIRED
- 任一端口 BLOCKED 返回 ALC.ELIGIBILITY_BLOCKED 并记录来源端口与原因码
- 任一端口 UNKNOWN 或规则版本不匹配返回 ALC.APPLICABILITY_UNKNOWN 并阻断
- 同对象存在活跃破坏性分配返回 ALC.DESTRUCTIVE_CONFLICT
- 无能力或跨范围请求返回 ALC.NOT_AUTHORIZED
- 旧 expectedCurrentVersion 返回 ALC.EXPECTED_VERSION_CONFLICT
- 重复释放或释放不存在分配返回 ALC.VALIDATION_FAILED
- 持久化、审计或 Outbox 失败时整体回滚
- 端口评估与分配提交之间的窗口不承诺跨模块原子性，事实固定评估时点版本

## 领域不变量

- 已创建分配不可修改或删除，释放只能追加且至多一次
- 同一物理对象分配序列只存在一个当前最高版本
- 分配固定身份映射版本但不改写实物实际身份（OPS-ALLOC-004）
- 活跃破坏性分配阻断同对象一切新分配，非破坏性可并存
- 三端口决定原样固定，UNKNOWN 等同阻断
- 破坏性为调用方声明属性，不决定 OD-004 产品/方法级主数据
- 不读取 receiving/scope/quantity 私有表，仅消费版本化公共端口
- 不创建任务、批次或 UsageEvent

## 数据契约

```json
{
  "allocation": [
    "allocationId",
    "subjectType/ref/version",
    "identityAssignmentRef/version",
    "scopeMatrixId/matrixVersion/scopeLineId",
    "planStepRef/version",
    "purpose",
    "sequenceOrder",
    "destructive",
    "requestedAmount/dimension/unit",
    "reservationEntryId?",
    "storageConditionRef/version",
    "validUntil",
    "state",
    "subjectAllocationVersion",
    "receivingGate{decision,itemVersion,releaseDecisionId,releaseDecisionVersion,ruleSetVersion}",
    "scopeGate{decision,matrixVersion,ruleSetVersion}",
    "quantityGate{decision,accountVersion,availableAmount,ruleSetVersion}",
    "assignedBy",
    "assignedAt"
  ],
  "gateActions": [
    "TEST_ASSIGNMENT（代码契约常量，语义即规格 TEST_OBJECT_ALLOCATION）"
  ],
  "release": [
    "allocationId",
    "reason",
    "releasedBy",
    "releasedAt"
  ],
  "states": [
    "ACTIVE",
    "RELEASED"
  ]
}
```

## API / 命令契约

```json
{
  "errors": [
    "ALC.VALIDATION_FAILED",
    "ALC.ALLOCATION_EXPIRED",
    "ALC.ELIGIBILITY_BLOCKED",
    "ALC.APPLICABILITY_UNKNOWN",
    "ALC.DESTRUCTIVE_CONFLICT",
    "ALC.NOT_AUTHORIZED",
    "ALC.OBJECT_NOT_ACCESSIBLE",
    "ALC.EXPECTED_VERSION_CONFLICT",
    "ALC.PERSISTENCE_UNAVAILABLE"
  ],
  "operations": [
    "POST /api/v1/test-object-allocations",
    "POST /api/v1/test-object-allocations/{id}/release",
    "GET /api/v1/test-object-allocations/{id}",
    "GET /api/v1/test-object-allocations/{id}/status"
  ],
  "publicPort": "AllocationStatusPort@v1",
  "success": [
    "201 TestObjectAllocationResult",
    "201 AllocationReleaseResult",
    "200 TestObjectAllocationResult",
    "200 AllocationStatusResult"
  ]
}
```

## 状态转换

- NONE -> ACTIVE by 三端口全 ALLOWED 的原子创建
- ACTIVE -> RELEASED by 一次性追加释放
- 任何失败不创建事实也不推进对象分配版本

## 权限与职责分离

- Allocation 模块只新增并校验 allocation.assign 单一能力和既有对象范围
- 被消费的 Receiving/Scope/Quantity 端口按各自已发布契约校验既有能力（Receiving 放行批准、scope.approve、quantity.post），本卡不放宽也不复制这些校验
- 不新增草稿编辑、发起/复核双人链或多级签署
- 客户端不能提交 OrganizationGroup
- 服务端对分配对象范围统一校验

## 审计要求

- 记录命令类型、allocationId、subject 摘要、三端口决定与版本、actor、correlationId 和结果
- 失败、越权、版本冲突与事务回滚通过独立追加路径记录
- Outbox eventId 与分配事实一一对应
- 敏感正文不写日志或指标

## UX 状态

- 本卡不新增前端页面
- HTTP 响应返回服务端计算的分配状态、对象分配版本和三端口固定决定
- 客户端不得自行推断资格、绕过端口门禁或把 UNKNOWN 当作允许

## 可观测性

- allocation_assigned_total 按 destructive 聚合
- allocation_gate_total 按端口与 ALLOWED/BLOCKED/UNKNOWN 聚合
- allocation_rejected_total 按稳定原因聚合
- UNKNOWN、事务回滚和 Outbox 积压写结构化告警

## 测试场景

| ID | 类型 | Given | When | Then |
|---|---|---|---|---|
| TC-ALLOC-001-01 | positive | 引用完整且授权有效；三端口均 ALLOWED | 创建分配 | 创建 ACTIVE 分配并固定三端口决定与版本；审计与 Outbox 同事务提交；状态查询 ALLOWED |
| TC-ALLOC-001-02 | boundary | 同一对象已有活跃非破坏性分配 | 再创建非破坏性分配、创建破坏性分配、之后再创建任意分配 | 非破坏性可并存；破坏性创建成功后同对象新分配返回 ALC.DESTRUCTIVE_CONFLICT；释放破坏性分配后恢复 |
| TC-ALLOC-001-03 | negative | Receiving、Scope 或 Quantity 端口返回 BLOCKED | 创建分配 | ALC.ELIGIBILITY_BLOCKED 且记录来源端口；不产生事实或成功事件 |
| TC-ALLOC-001-04 | negative | 端口返回 UNKNOWN 或 validUntil 已过期 | 创建分配 | ALC.APPLICABILITY_UNKNOWN 或 ALC.ALLOCATION_EXPIRED；无副作用 |
| TC-ALLOC-001-05 | permission | 缺少 capability 或对象范围 | 创建、释放或查询 | 统一拒绝；追加脱敏失败审计 |
| TC-ALLOC-001-06 | concurrency | 两个调用使用相同 expectedCurrentVersion | 并发创建同对象分配 | 最多一笔成功；另一笔版本冲突；对象分配版本只推进一次 |
| TC-ALLOC-001-07 | recovery | 审计或 Outbox 失败 | 创建并重试 | 首笔全部回滚；重试只创建一个逻辑分配 |
| TC-ALLOC-001-08 | regression | 存在 ACTIVE 分配 | 尝试改写历史、重复释放和查询旧版本状态 | 数据库拒绝 UPDATE/DELETE；释放至多一次；旧对象分配版本状态查询 UNKNOWN |

## 明确非目标

- 不实现 UsageEvent、领用、实际消耗或归还量记录
- 不实现 CoverageDecision 代表性覆盖
- 不实现计划/任务/批次生成
- 不实现人员、设备、场地、夹具资源排程（10.7-7）
- 不实现复合样
- 不决定 OD-004 产品/方法级破坏性主数据
- 不新增前端工作台
- 不修改 Release baseline
- 不创建 Seal、tag、GitHub Release 或部署
- 不实现共享 SaaS 多租户

## 允许修改路径

- `spec/requirements/BUS-ALLOC-001__v1.0.0.json`
- `spec/requirements/BUS-ALLOC-002__v1.0.0.json`
- `spec/requirements/BUS-ALLOC-003__v1.0.0.json`
- `spec/acceptance/AC-ELEC-003__v1.0.0.json`
- `spec/stories/ATC-ALLOC-001__v1.0.0.json`
- `generated/spec/**`
- `.planning/2026-07-26-dev-010-allocation-eligibility/**`
- `OpenLIMS.slnx`
- `contracts/allocation/**`
- `src/modules/allocation/**`
- `src/host/api/**`
- `src/host/worker/**`
- `tests/architecture/**`
- `tests/unit/allocation/**`
- `tests/contract/allocation/**`
- `tests/integration/allocation/**`
- `tests/e2e/allocation/**`
- `tests/contract/labeling/OpenLIMS.Labeling.ContractTests/packages.lock.json`
- `tests/contract/platform/OpenLIMS.Platform.ContractTests/packages.lock.json`
- `tests/contract/receiving/OpenLIMS.Receiving.ContractTests/packages.lock.json`
- `tests/contract/scope/OpenLIMS.Scope.ContractTests/packages.lock.json`
- `tests/contract/quantity/OpenLIMS.Quantity.ContractTests/packages.lock.json`
- `tests/integration/platform/OpenLIMS.Platform.IntegrationTests/packages.lock.json`
- `tests/test_repository_contract.py`
- `docs/domain/allocation/**`
- `scripts/verify.ps1`
- `scripts/verify.sh`

## 验证命令

- `python -m tools.specgen ready --story ATC-ALLOC-001@1.0.0`
- `pwsh -File scripts/verify.ps1 -Profile task -Module allocation`
- `pwsh -File scripts/verify.ps1 -Profile architecture`
- `pwsh -File scripts/verify.ps1 -Profile contracts`
- `python -m tools.specgen check`

## 完成定义

- 追加迁移不改写 DEV-003 至 DEV-009 之前的历史
- 分配事实完整、不可变且固定三端口决定与版本
- 破坏性互斥、并发、过期和 UNKNOWN 始终失败关闭且无副作用
- 权限、事务、恢复、审计和 Outbox 测试通过
- 公共状态端口只依据精确对象分配版本
- 无跨模块私表访问且仅消费版本化公共端口
- 全仓验证通过且二次 generate written=0
- 所有变更位于 allowed_paths

## AI 执行约束

- 不得修改本文件；它由结构化规格生成。
- 不得把待决策项自行解释为默认业务规则。
- 不得访问其他模块私有表；必须使用批准的端口或事件契约。
- 若前置决策、依赖或测试夹具缺失，应停止实现并报告阻塞，不得猜测。

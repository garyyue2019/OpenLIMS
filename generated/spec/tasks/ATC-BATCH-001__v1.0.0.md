<!-- GENERATED FILE — DO NOT EDIT.
Generator: openlims-specgen@0.1.0
Sources: ATC-BATCH-001@1.0.0
Edit files under spec/ and run `python -m tools.specgen generate`.
-->

# ATC-BATCH-001：实施 DEV-013 制备/分析批最小切片

## 元数据

| 字段 | 值 |
|---|---|
| 规格版本 | `1.0.0` |
| 评审状态 | `approved` |
| 目标发布 | `REL-R1-RECEIVING-PILOT@1.0.0` |
| Epic | `EP-EXECUTION` |
| Feature | `FEAT-BATCH-MANAGEMENT` |
| 开发就绪度 | `ready` |
| 变更级别 | `major` |
| 负责人角色 | 实验室技术负责人, 技术负责人, 质量负责人, QA负责人 |
| 影响模块 | batch, allocation, qc, raw-data, authorization, audit, outbox, automated-test |
| 来源 | PRD-MAIN#OD-030, PRD-MAIN#OPS-BATCH-001, PRD-MAIN#OPS-BATCH-002, PRD-MAIN#OPS-BATCH-003, PRD-MAIN#AC-BATCH-001 |
| 固定依赖 | ATC-PLT-003@1.0.0, ED-001@2.0.0, OD-002@1.0.0, OD-030@1.0.0, ATC-ALLOC-001@1.0.0, BUS-BATCH-001@1.0.0, BUS-BATCH-002@1.0.0, BUS-BATCH-003@1.0.0, AC-BATCH-001@1.0.0, SEC-AUTH-001@1.0.0, SEC-AUD-001@2.0.0, NFR-ARCH-001@2.0.0 |
| 规格指纹 | `6028422fb5b08d932fa1a3d310e0513b436edee083e38cb2ab109cfcda6df3af` |

## 业务结果

实验室可以把跨委托的试样与批准 QC 组成责任清晰的类型化批次；QC 失败时整批影响被系统性冻结且不可选择性重开，原始数据权威保留在源系统并以不可变引用可追溯。

## 主要参与者

具有 batch.manage 及法人、实验室对象范围的批次负责人

## 触发条件

批次负责人创建批次、添加成员、追加外部证据或提交冻结事件

## 前置条件

- DEV-010 已交付 AllocationStatusPort
- 部署绑定唯一 OrganizationGroup
- 调用身份由服务端建立
- 外部证据由调用方提交稳定 ID、版本和 SHA-256 哈希

## 正常路径

- 校验 actor capability 和法人/实验室对象范围
- 创建显式类型批次（禁止通用 ExecutionRun）
- 试样成员：在独立事务评估 AllocationStatusPort，ALLOWED 后在批次事务内固定端口决定与分配版本
- QC 样成员：固定批准 QC 引用与版本
- 追加外部证据引用（来源系统+稳定 ID+版本+SHA-256）
- QC 失败提交冻结事件：整批成员冻结、原数据保留、批准后续处理以引用记录
- 公共 BatchStatusPort 对当前批次版本返回 ALLOWED

## 失败路径

- 未知批次类型或冻结原因返回 BAT.VALIDATION_FAILED
- 分配门禁 BLOCKED/UNKNOWN 或端口异常返回 BAT.ELIGIBILITY_BLOCKED / BAT.APPLICABILITY_UNKNOWN 并失败关闭
- 同一分配重复入批返回 BAT.VALIDATION_FAILED
- 冻结后新增成员或证据返回 BAT.BATCH_FROZEN
- 无能力或跨范围请求返回 BAT.NOT_AUTHORIZED
- 旧 expectedCurrentVersion 返回 BAT.EXPECTED_VERSION_CONFLICT
- 持久化、审计或 Outbox 失败整体回滚
- 证据哈希格式非法返回 BAT.VALIDATION_FAILED

## 领域不变量

- 批次类型与冻结原因为显式最小枚举
- 批次事实、成员、证据和冻结事件创建后不可修改或删除
- 一个批次只存在一个当前最高版本，所有变更推进版本
- 成员各自固定客户/委托归属，批级操作不覆盖成员归属
- 冻结作用于整批，不允许选择性冻结或重开
- UNKNOWN 等同阻断
- 不读取 allocation/receiving/scope/quantity 私有表，仅消费版本化公共端口
- LIMS 不复制外部权威内容（OD-030）

## 数据契约

```json
{
  "batch": [
    "batchId",
    "batchType(PREPARATION/PRECONDITIONING/ANALYTICAL/INSTRUMENT_RUN)",
    "legalEntityId",
    "laboratoryId",
    "version",
    "ruleSetVersion",
    "state(ACTIVE/FROZEN)",
    "createdBy",
    "createdAt"
  ],
  "evidence": [
    "evidenceId",
    "sourceSystem(CDS/ELN/INSTRUMENT)",
    "externalRef/version",
    "sha256",
    "recordedBy",
    "recordedAt"
  ],
  "freeze": [
    "freezeId",
    "cause(QC_FAILURE/ENVIRONMENT_OUT_OF_TOLERANCE/CALIBRATION_INVALID)",
    "affectedMemberCount",
    "approvedFollowUpRef?/version",
    "frozenBy",
    "frozenAt"
  ],
  "member": [
    "memberId",
    "memberType(SPECIMEN/QC_SAMPLE)",
    "allocationId?/subjectAllocationVersion?",
    "allocationGate{decision,ruleSetVersion}?",
    "qcRef?/version",
    "customerId",
    "serviceOrderId",
    "productCategory",
    "addedBy",
    "addedAt"
  ]
}
```

## API / 命令契约

```json
{
  "errors": [
    "BAT.VALIDATION_FAILED",
    "BAT.ELIGIBILITY_BLOCKED",
    "BAT.APPLICABILITY_UNKNOWN",
    "BAT.BATCH_FROZEN",
    "BAT.NOT_AUTHORIZED",
    "BAT.OBJECT_NOT_ACCESSIBLE",
    "BAT.EXPECTED_VERSION_CONFLICT",
    "BAT.PERSISTENCE_UNAVAILABLE"
  ],
  "operations": [
    "POST /api/v1/batches",
    "POST /api/v1/batches/{id}/members",
    "POST /api/v1/batches/{id}/evidence",
    "POST /api/v1/batches/{id}/freeze",
    "GET /api/v1/batches/{id}",
    "GET /api/v1/batches/{id}/status"
  ],
  "publicPort": "BatchStatusPort@v1",
  "success": [
    "201 BatchResult",
    "201 BatchMemberResult",
    "201 BatchEvidenceResult",
    "201 BatchFreezeResult",
    "200 BatchResult",
    "200 BatchStatusResult"
  ]
}
```

## 状态转换

- NONE -> ACTIVE@v1 by 创建
- ACTIVE@vN -> ACTIVE@vN+1 by 追加成员或证据
- ACTIVE@vN -> FROZEN@vN+1 by 一次性冻结事件
- 任何失败不产生事实也不推进版本

## 权限与职责分离

- Batch 模块只新增并校验 batch.manage 单一能力和法人/实验室对象范围（批次跨客户/委托，成员级归属单独固定）
- 被消费的 AllocationStatusPort 按其已发布契约校验 allocation.assign，本卡不放宽也不复制
- 不新增草稿编辑或多级签署
- 客户端不能提交 OrganizationGroup

## 审计要求

- 记录命令类型、batchId/version、成员与证据摘要、冻结原因、actor、correlationId 和结果
- 失败、越权、版本冲突与事务回滚通过独立追加路径记录
- Outbox eventId 与批次事实一一对应
- 敏感正文不写日志或指标

## UX 状态

- 本卡不新增前端页面
- HTTP 响应返回服务端计算的批次状态、版本与冻结事实
- 客户端不得自行推断批次资格、绕过分配门禁或把 UNKNOWN 当作允许

## 可观测性

- batch_created_total 按 batchType 聚合
- batch_member_total 按 memberType 聚合
- batch_frozen_total 按 cause 聚合
- batch_gate_total 与 batch_rejected_total 按决定/原因聚合
- UNKNOWN、事务回滚和 Outbox 积压写结构化告警

## 测试场景

| ID | 类型 | Given | When | Then |
|---|---|---|---|---|
| TC-BATCH-001-01 | positive | 分配状态端口 ALLOWED | 创建分析批并添加试样成员与 QC 样 | 批次 ACTIVE 且成员固定端口决定与归属；审计与 Outbox 同事务提交 |
| TC-BATCH-001-02 | boundary | 三个不同委托的试样成员 | 入批并读取 | 每个成员保留自身客户/委托归属；批级字段不覆盖成员归属 |
| TC-BATCH-001-03 | negative | 分配端口 BLOCKED/UNKNOWN 或异常 | 添加试样成员 | 失败关闭且无事实；原因与来源记录 |
| TC-BATCH-001-04 | negative | 同一分配已入批或未知类型/原因 | 再次提交 | BAT.VALIDATION_FAILED；无副作用 |
| TC-BATCH-001-05 | permission | 缺少 capability 或法人/实验室范围 | 任一操作 | 统一拒绝；追加脱敏失败审计 |
| TC-BATCH-001-06 | concurrency | 两个调用使用相同 expectedCurrentVersion | 并发添加成员 | 最多一笔成功；另一笔版本冲突 |
| TC-BATCH-001-07 | recovery | 审计或 Outbox 失败 | 提交并重试 | 首笔全部回滚；重试只产生一个逻辑事实 |
| TC-BATCH-001-08 | regression | 含三委托成员与 QC 的批次；QC 失败 | 冻结后尝试新增成员/证据、改写历史和查询状态 | 整批冻结且原数据保留；新增被拒、数据库拒绝改写；状态 BLOCKED，不得选择性重开 |

## 明确非目标

- 不实现 QC 结果判定或环境监控采集（冻结原因由授权人声明）
- 不实现结果录入、采纳或报告（后续卡）
- 不实现仪器驱动、CDS/ELN 集成或内容镜像（OD-030）
- 不实现资源排程
- 不新增前端工作台
- 不修改 Release baseline，不创建 Seal、tag、GitHub Release 或部署

## 允许修改路径

- `spec/decisions/OD-030__v1.0.0.json`
- `spec/requirements/BUS-BATCH-001__v1.0.0.json`
- `spec/requirements/BUS-BATCH-002__v1.0.0.json`
- `spec/requirements/BUS-BATCH-003__v1.0.0.json`
- `spec/acceptance/AC-BATCH-001__v1.0.0.json`
- `spec/stories/ATC-BATCH-001__v1.0.0.json`
- `generated/spec/**`
- `.planning/2026-07-26-dev-013-batch-management/**`
- `OpenLIMS.slnx`
- `contracts/batch/**`
- `src/modules/batch/**`
- `src/host/api/**`
- `src/host/worker/**`
- `tests/architecture/**`
- `tests/unit/batch/**`
- `tests/contract/batch/**`
- `tests/integration/batch/**`
- `tests/e2e/batch/**`
- `tests/contract/labeling/OpenLIMS.Labeling.ContractTests/packages.lock.json`
- `tests/contract/platform/OpenLIMS.Platform.ContractTests/packages.lock.json`
- `tests/contract/receiving/OpenLIMS.Receiving.ContractTests/packages.lock.json`
- `tests/contract/scope/OpenLIMS.Scope.ContractTests/packages.lock.json`
- `tests/contract/quantity/OpenLIMS.Quantity.ContractTests/packages.lock.json`
- `tests/contract/allocation/OpenLIMS.Allocation.ContractTests/packages.lock.json`
- `tests/integration/platform/OpenLIMS.Platform.IntegrationTests/packages.lock.json`
- `tests/test_repository_contract.py`
- `docs/domain/batch/**`
- `scripts/verify.ps1`
- `scripts/verify.sh`

## 验证命令

- `python -m tools.specgen ready --story ATC-BATCH-001@1.0.0`
- `pwsh -File scripts/verify.ps1 -Profile task -Module batch`
- `pwsh -File scripts/verify.ps1 -Profile architecture`
- `pwsh -File scripts/verify.ps1 -Profile contracts`
- `python -m tools.specgen check`

## 完成定义

- 追加迁移不改写既有模块历史
- 批次、成员、证据与冻结事实完整、不可变且版本固定
- 分配门禁、客户隔离、整批冻结和 UNKNOWN 始终失败关闭
- 权限、并发、事务、恢复、审计和 Outbox 测试通过
- 公共状态端口只依据精确批次版本
- 无跨模块私表访问
- 全仓验证通过且二次 generate written=0
- 所有变更位于 allowed_paths

## AI 执行约束

- 不得修改本文件；它由结构化规格生成。
- 不得把待决策项自行解释为默认业务规则。
- 不得访问其他模块私有表；必须使用批准的端口或事件契约。
- 若前置决策、依赖或测试夹具缺失，应停止实现并报告阻塞，不得猜测。

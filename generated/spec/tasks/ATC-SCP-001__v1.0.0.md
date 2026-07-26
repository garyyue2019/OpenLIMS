<!-- GENERATED FILE — DO NOT EDIT.
Generator: openlims-specgen@0.1.0
Sources: ATC-SCP-001@1.0.0
Edit files under spec/ and run `python -m tools.specgen generate`.
-->

# ATC-SCP-001：实施 DEV-008 ScopeLine 生产可用门禁

## 元数据

| 字段 | 值 |
|---|---|
| 规格版本 | `1.0.0` |
| 评审状态 | `approved` |
| 目标发布 | `REL-R1-RECEIVING-PILOT@1.0.0` |
| Epic | `EP-SCOPE-COMMERCIAL` |
| Feature | `FEAT-SCOPE-LINE-GATE` |
| 开发就绪度 | `ready` |
| 变更级别 | `major` |
| 负责人角色 | 技术负责人, 质量负责人, 产品负责人, QA负责人 |
| 影响模块 | scope, authorization, audit, outbox, production-gate, automated-test |
| 来源 | PRD-MAIN#OD-027, PRD-MAIN#BUS-SCOPE-001, PRD-MAIN#BUS-SCOPE-002, PRD-MAIN#BUS-SCOPE-003, PRD-MAIN#AC-SCOPE-001 |
| 固定依赖 | ATC-PLT-003@1.0.0, ED-001@2.0.0, OD-002@1.0.0, OD-027@1.0.0, BUS-SCOPE-001@1.0.0, BUS-SCOPE-002@1.0.0, BUS-SCOPE-003@1.0.0, AC-SCOPE-001@1.0.0, SEC-AUTH-001@1.0.0, SEC-AUD-001@2.0.0, NFR-ARCH-001@2.0.0 |
| 规格指纹 | `1ae1eaf0c359f3c045dddcb33fd10e0b2543e9b8f68aa7631577fa791be20503` |

## 业务结果

授权技术人员可以把完整检测范围固定为不可变批准版本；任何下游在创建生产事实前可用公共端口验证精确矩阵版本是否具备生产资格。

## 主要参与者

具有 scope.approve 及法人、实验室、客户、委托和产品类别对象范围的技术批准人

## 触发条件

授权技术人员提交初始或后继 TestScopeMatrix 批准版本

## 前置条件

- DEV-002 模块接入通道已交付
- 部署绑定唯一 OrganizationGroup
- 调用身份由服务端建立
- 范围引用由调用方提交精确稳定 ID 和版本

## 正常路径

- 校验 actor capability 和对象范围
- 校验 expectedCurrentVersion 与锁内当前版本
- 校验全部 ScopeLine 必需引用和 EvaluationMode 条件
- 创建稳定 matrixId 或追加 version+1
- 原子保存不可变矩阵版本、范围行、审计和 Outbox
- 公共 ScopeProductionEligibilityPort 对当前完整版本返回 ALLOWED

## 失败路径

- 缺失必需引用或重复范围行时返回 SCOPE_VALIDATION_FAILED
- EVALUATED 缺少限值或判定规则时返回 SCOPE_EVALUATION_INCOMPLETE
- 非 EVALUATED 携带冲突结论字段时返回 SCOPE_EVALUATION_CONFLICT
- 无能力或跨范围请求返回 SCOPE_NOT_AUTHORIZED
- 旧 expectedCurrentVersion 返回 EXPECTED_VERSION_CONFLICT
- 未知模式、规则版本或资格查询版本返回 UNKNOWN 并阻断
- 持久化、审计或 Outbox 失败时整体回滚

## 领域不变量

- 批准矩阵版本和范围行不可修改或删除
- 一个矩阵只存在一个当前最高批准版本
- 一个范围行只绑定一个对象或特征、市场/要求、项目和方法选项
- 规则升级不自动重算既有版本
- UNKNOWN 等同阻断
- ScopeLine 不承载身份映射、任务分配或代表性覆盖
- 不读取其他模块私表且不创建生产任务

## 数据契约

```json
{
  "evaluationModes": [
    "MEASURED_ONLY",
    "EVALUATED",
    "NOT_EVALUATED",
    "WAIVED"
  ],
  "line": [
    "scopeLineId",
    "subjectType/ref/version",
    "targetMarketRef/version",
    "requirementClauseRef/version",
    "testItemRef/version",
    "methodRef/version",
    "methodOption",
    "sampleRequirementRef/version",
    "evaluationMode",
    "workCenterRef/version",
    "reportPosition",
    "conditionalReferences"
  ],
  "matrix": [
    "scopeMatrixId",
    "version",
    "ruleSetVersion",
    "approvedBy",
    "approvedAt",
    "lines"
  ]
}
```

## API / 命令契约

```json
{
  "errors": [
    "SCOPE_VALIDATION_FAILED",
    "SCOPE_EVALUATION_INCOMPLETE",
    "SCOPE_EVALUATION_CONFLICT",
    "SCOPE_NOT_AUTHORIZED",
    "EXPECTED_VERSION_CONFLICT",
    "SCOPE_APPLICABILITY_UNKNOWN",
    "PERSISTENCE_UNAVAILABLE"
  ],
  "operations": [
    "POST /api/v1/scope-matrices",
    "POST /api/v1/scope-matrices/{id}/versions",
    "GET /api/v1/scope-matrices/{id}/versions/{version}",
    "GET /api/v1/scope-matrices/{id}/production-eligibility"
  ],
  "publicPort": "ScopeProductionEligibilityPort@v1",
  "success": [
    "201 ScopeMatrixVersionResult",
    "200 ScopeMatrixVersionResult",
    "200 ScopeProductionEligibilityResult"
  ]
}
```

## 状态转换

- NONE -> APPROVED@v1
- APPROVED@vN -> APPROVED@vN+1 by append-only revision
- 任何失败不创建新版本

## 权限与职责分离

- 初始和后继批准只要求 scope.approve 单一能力和既有对象范围
- 不新增草稿编辑、发起/复核双人链或多级签署
- 客户端不能提交 OrganizationGroup
- 服务端对每行对象范围统一校验

## 审计要求

- 记录命令类型、matrixId/version、全部固定引用摘要、actor、correlationId 和结果
- 失败、越权、版本冲突与事务回滚通过追加路径记录
- Outbox eventId 与矩阵版本一一对应
- 敏感正文不写日志或指标

## UX 状态

- 本卡不新增前端页面
- HTTP 响应返回服务端计算的完整性、固定版本和资格决定
- 客户端不得自行补齐引用、推断 EvaluationMode 或把 UNKNOWN 当作允许

## 可观测性

- scope_matrix_approved_total 按 initial/revision 聚合
- scope_gate_total 按 ALLOWED/BLOCKED/UNKNOWN 聚合
- scope_rejected_total 按稳定原因聚合
- UNKNOWN、事务回滚和 Outbox 积压写结构化告警

## 测试场景

| ID | 类型 | Given | When | Then |
|---|---|---|---|---|
| TC-SCP-001-01 | positive | 全部引用完整；授权有效 | 提交 v1 | 创建 APPROVED@v1；资格 ALLOWED |
| TC-SCP-001-02 | boundary | 包含四种 EvaluationMode | 提交版本 | 仅 EVALUATED 要求限值与判定规则；其他模式保存各自依据 |
| TC-SCP-001-03 | negative | 缺少必需引用或结论字段与模式冲突 | 提交版本 | 稳定错误；不创建事实或成功事件 |
| TC-SCP-001-04 | negative | 仅有客户、套餐、BOM 或 AI 候选 | 查询生产资格 | BLOCKED 或 UNKNOWN；无生产副作用 |
| TC-SCP-001-05 | permission | 缺少 capability 或对象范围 | 提交或读取 | 统一拒绝；追加脱敏失败审计 |
| TC-SCP-001-06 | concurrency | 两个调用使用相同 expectedCurrentVersion | 并发提交 | 仅一个创建后继版本；另一个版本冲突 |
| TC-SCP-001-07 | recovery | 审计或 Outbox 失败 | 提交并重试 | 首笔全部回滚；重试只创建一个逻辑版本 |
| TC-SCP-001-08 | regression | v2 已批准 | 读取 v1 或尝试修改历史 | v1 可只读重建；旧版本生产资格 UNKNOWN；数据库拒绝改写 |

## 明确非目标

- 不实现报价或合同
- 不生成生产任务
- 不实现 TestObjectAllocation
- 不实现 CoverageDecision
- 不建设要求/方法/样品需求主数据模块
- 不新增前端工作台
- 不修改 Release baseline
- 不创建 Seal、tag、GitHub Release 或部署
- 不实现共享 SaaS 多租户

## 允许修改路径

- `spec/decisions/OD-027__v1.0.0.json`
- `spec/requirements/BUS-SCOPE-001__v1.0.0.json`
- `spec/requirements/BUS-SCOPE-002__v1.0.0.json`
- `spec/requirements/BUS-SCOPE-003__v1.0.0.json`
- `spec/acceptance/AC-SCOPE-001__v1.0.0.json`
- `spec/stories/ATC-SCP-001__v1.0.0.json`
- `generated/spec/**`
- `.planning/2026-07-26-dev-008-scopeline-gate/**`
- `OpenLIMS.slnx`
- `contracts/scope/**`
- `src/modules/scope/**`
- `src/host/api/**`
- `src/host/worker/**`
- `tests/architecture/**`
- `tests/unit/scope/**`
- `tests/contract/scope/**`
- `tests/integration/scope/**`
- `tests/e2e/scope/**`
- `tests/contract/labeling/OpenLIMS.Labeling.ContractTests/packages.lock.json`
- `tests/contract/platform/OpenLIMS.Platform.ContractTests/packages.lock.json`
- `tests/contract/receiving/OpenLIMS.Receiving.ContractTests/packages.lock.json`
- `tests/integration/platform/OpenLIMS.Platform.IntegrationTests/packages.lock.json`
- `tests/test_repository_contract.py`
- `docs/domain/scope/**`
- `scripts/verify.ps1`
- `scripts/verify.sh`

## 验证命令

- `python -m tools.specgen ready --story ATC-SCP-001@1.0.0`
- `pwsh -File scripts/verify.ps1 -Profile task -Module scope`
- `pwsh -File scripts/verify.ps1 -Profile architecture`
- `pwsh -File scripts/verify.ps1 -Profile contracts`
- `python -m tools.specgen check`

## 完成定义

- 追加迁移不改写 DEV-003 至 DEV-008 之前的历史
- 批准版本和范围行完整、不可变且版本固定
- 四种 EvaluationMode 条件语义通过边界测试
- 候选、缺失、旧版本和 UNKNOWN 始终失败关闭且无生产副作用
- 权限、并发、事务、恢复、审计和 Outbox 测试通过
- 公共资格端口只依据精确版本
- 无跨模块私表访问
- 全仓验证通过且二次 generate written=0
- 所有变更位于 allowed_paths

## AI 执行约束

- 不得修改本文件；它由结构化规格生成。
- 不得把待决策项自行解释为默认业务规则。
- 不得访问其他模块私有表；必须使用批准的端口或事件契约。
- 若前置决策、依赖或测试夹具缺失，应停止实现并报告阻塞，不得猜测。

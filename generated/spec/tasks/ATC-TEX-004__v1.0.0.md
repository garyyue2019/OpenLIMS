<!-- GENERATED FILE — DO NOT EDIT.
Generator: openlims-specgen@0.1.0
Sources: ATC-TEX-004@1.0.0
Edit files under spec/ and run `python -m tools.specgen generate`.
-->

# ATC-TEX-004：实施 DEV-028 纺织样品需求与 CuttingPlan 运行时

## 元数据

| 字段 | 值 |
|---|---|
| 规格版本 | `1.0.0` |
| 评审状态 | `approved` |
| 目标发布 | `REL-R1-RECEIVING-PILOT@1.0.0` |
| Epic | `EP-EXECUTION` |
| Feature | `FEAT-TEXTILE-SAMPLE-REQUIREMENT-RUNTIME` |
| 开发就绪度 | `ready` |
| 变更级别 | `major` |
| 负责人角色 | 实验室技术负责人, 纺织行业包负责人, 质量负责人, QA负责人 |
| 影响模块 | textile, sample-requirement, cutting-plan, destructive-exclusion, technical-approval, runtime-module, audit, outbox, automated-test |
| 来源 | PRD-MAIN#OPS-TEXTILE-001, PRD-MAIN#OPS-TEXTILE-002, PRD-MAIN#OPS-TEXTILE-003, PRD-MAIN#AC-TEXTILE-001 |
| 固定依赖 | ED-001@2.0.0, OD-001@1.0.0, OD-036@1.0.0, OD-002@1.0.0, OD-027@1.0.0, BUS-TEX-001@1.0.0, BUS-TEX-002@1.0.0, BUS-TEX-003@1.0.0, AC-TEXTILE-001@1.0.0, BUS-TEX-006@1.0.0, BUS-TEX-007@1.0.0, BUS-TEX-008@1.0.0, AC-TEXTILE-004@1.0.0, ATC-TEX-001@1.0.0, ATC-PLT-003@1.0.0, SEC-AUTH-001@1.0.0, SEC-AUD-001@2.0.0, NFR-ARCH-001@2.0.0 |
| 规格指纹 | `9e64c1aeb3589b0f0dbbab8f6a2a73973d994526632f44f03ae7d534b2714989` |

## 业务结果

纺织技术人员能够以精确版本输入计算并保存可解释的样品需求，看到按款色部件部位聚合的面积缺口，形成来源可追溯的 CuttingPlan；只有样品充足、规则已知且结构有效的计划才能被具备明确能力的人员批准，互斥裁样、权限、并发或证据写入失败不会产生半完成事实。

## 主要参与者

编制纺织样品需求和 CuttingPlan 的技术人员、批准 CuttingPlan 的授权人员，以及消费批准状态的下游模块

## 触发条件

技术人员为一组固定来源布批/实物和检测项目创建样品需求计算或 CuttingPlan，或在输入变化后以 expectedCurrentVersion 追加计划后继版本

## 前置条件

- OD-036@1.0.0 已批准纺织运行时实现与受控验证，OD-001@1.0.0 的玩具唯一生产试点保持不变
- ATC-TEX-001 已冻结样品需求、互斥共享与 CuttingPlan 序列化契约
- 平台请求上下文、对象级授权、事务审计与 Outbox 公共能力已交付
- 所有输入均提供稳定 ID、精确版本和 TextileContract.RuleSetVersion，不允许解析最新版

## 正常路径

- 提交样品需求计算，固定组织范围、需求行、可用面料、规则集和输入哈希
- 复用已批准纯规则确定性计算试样数、面积、共享组和缺口，并追加保存不可变计算版本
- 结果为 SUFFICIENT 时创建 CuttingPlan 草案，固定样品需求版本、来源实物/布批、取样部位、方向、尺寸、数量、距布边、模板、操作人和生成试样
- 系统校验 CuttingPlan 结构、样品需求决定、规则集和输入哈希一致
- 具备 textile.cutting-plan.approve 且通过对象范围授权的人员批准计划，批准事实与计划版本冻结
- 业务事实、audit_intent 与 outbox 同事务提交，返回可重建计划与批准概览
- ITextileCuttingPlanStatusPort@v1 按计划 ID、版本和规则集返回 ALLOWED/BLOCKED/UNKNOWN

## 失败路径

- 样品面积不足 → 保存 INSUFFICIENT 缺口和补样/范围变更 Outbox 证据；批准以 TEX.SAMPLE_REQUIREMENT_NOT_APPROVABLE 拒绝
- 未知规则集或方向 → UNKNOWN 或 TEX.DIRECTION_UNKNOWN，等同阻断且不得批准
- 跨互斥组共享或破坏性共享 → TEX.EXCLUSIVE_SHARE_REJECTED，不保存半完成计划
- CuttingPlan 字段、尺寸、数量、距布边或生成试样不一致 → TEX.VALIDATION_FAILED
- expectedCurrentVersion 不匹配或并发追加 → TEX.EXPECTED_VERSION_CONFLICT
- 行为人无 textile.sample-requirement.manage、无 textile.cutting-plan.approve 或对象范围不匹配 → TEX.NOT_AUTHORIZED 或 TEX.OBJECT_NOT_ACCESSIBLE
- 审计、Outbox 或模块持久化失败 → TEX.PERSISTENCE_UNAVAILABLE，整体回滚并追加独立失败尝试证据
- 状态端口收到未知计划版本、规则集或不完整证据 → UNKNOWN，调用方必须失败关闭

## 领域不变量

- 只消费精确版本输入和固定规则集；UNKNOWN 等同阻断，不从运行对象推导最新版
- 每行所需试样数等于平行数加复测预留加留样，面积与缺口保留逐维度来源
- 不同互斥破坏组或破坏性行不得共享同一裁片；仅非破坏性同规格行可共享
- INSUFFICIENT 或 UNKNOWN 的样品需求永远不能产生 APPROVED CuttingPlan
- 样品需求、计划、批准、审计和 Outbox 均追加式；已发布事实不可 UPDATE/DELETE
- textile 模块不访问 receiving、scope、quantity、allocation 或其他模块私表，只保存外部版本引用或消费公共端口
- 本卡不默认允许或禁止创建人自批；职责分离由授权策略显式决定，模块不得补默认值
- 现有 Textile v1 序列化契约保持兼容

## 数据契约

```json
{
  "approval": [
    "cuttingPlanId/version",
    "sampleRequirementId/version/inputHash/ruleSetVersion",
    "approvedBy/approvedAt/comment?",
    "correlationId"
  ],
  "cuttingPlan": [
    "cuttingPlanId/version/expectedCurrentVersion",
    "sampleRequirementId/version",
    "sourceItemRef/version",
    "samplingPosition/direction/lengthMm/widthMm/plannedCount/minDistanceFromSelvedgeMm",
    "templateVersion/operatorId/generatedSpecimenIds[]",
    "state(DRAFT/APPROVED/SUPERSEDED)",
    "inputHash/ruleSetVersion/createdBy/createdAt"
  ],
  "sampleRequirement": [
    "requirementId/version",
    "organizationGroupId from trusted request context",
    "ruleSetVersion",
    "demandLines[] and availableFabrics[] using v1 frozen contracts",
    "decision(SUFFICIENT/INSUFFICIENT/UNKNOWN)",
    "reasonCodes/specimenPlans/gaps",
    "inputHash",
    "createdBy/createdAt"
  ],
  "statusDecision": [
    "decision(ALLOWED/BLOCKED/UNKNOWN)",
    "reasonCodes",
    "cuttingPlanId/version",
    "sampleRequirementId/version",
    "ruleSetVersion"
  ]
}
```

## API / 命令契约

```json
{
  "errors": [
    "TEX.VALIDATION_FAILED",
    "TEX.DIRECTION_UNKNOWN",
    "TEX.EXCLUSIVE_SHARE_REJECTED",
    "TEX.APPLICABILITY_UNKNOWN",
    "TEX.SAMPLE_REQUIREMENT_NOT_APPROVABLE",
    "TEX.EXPECTED_VERSION_CONFLICT",
    "TEX.NOT_AUTHORIZED",
    "TEX.OBJECT_NOT_ACCESSIBLE",
    "TEX.PERSISTENCE_UNAVAILABLE"
  ],
  "operations": [
    "POST /api/v1/textile/sample-requirements → 201 保存不可变计算与 SUFFICIENT/INSUFFICIENT/UNKNOWN 结果",
    "POST /api/v1/textile/cutting-plans → 201 创建绑定精确样品需求版本的 DRAFT 计划",
    "POST /api/v1/textile/cutting-plans/{id}/versions/{version}/approval → 200 批准并冻结计划",
    "GET /api/v1/textile/cutting-plans/{id}/versions/{version} → 200 返回计划、样品需求、缺口和批准证据"
  ],
  "publicPort": "ITextileCuttingPlanStatusPort@v1：按 organization scope、cuttingPlanId、version、ruleSetVersion 返回 ALLOWED/BLOCKED/UNKNOWN；UNKNOWN 视为拒绝"
}
```

## 状态转换

- SampleRequirement：每次计算直接追加一个不可变 SUFFICIENT、INSUFFICIENT 或 UNKNOWN 版本；无原地状态变更
- CuttingPlan：DRAFT → APPROVED；APPROVED 只由更高计划版本派生 SUPERSEDED 视图，旧事实不改写
- 批准失败、并发冲突或证据写入失败不产生中间状态；重试必须使用幂等 correlationId 和当前 expectedCurrentVersion

## 权限与职责分离

- 创建样品需求、创建计划和查询要求 textile.sample-requirement.manage 与可信请求上下文中的对象范围
- 批准额外要求 textile.cutting-plan.approve 与同一对象范围
- 客户端不得提交 OrganizationGroup、approvedBy 或授权决定
- 本卡不决定自批策略；授权层若无明确策略，模块不自行推断职责分离默认值

## 审计要求

- 记录 CALCULATE_TEXTILE_SAMPLE_REQUIREMENT、CREATE_TEXTILE_CUTTING_PLAN、APPROVE_TEXTILE_CUTTING_PLAN 及对象版本、输入哈希、规则集和 correlationId
- 样品不足追加 TEXTILE_SAMPLE_SHORTAGE_DETECTED Outbox 证据，包含需求版本和缺口摘要引用，不写其他模块私表
- 业务事实、audit_intent 与 outbox 同事务；未授权、UNKNOWN、互斥冲突、并发和持久化失败走独立追加 audit_attempt
- 不得记录 Secret、原始客户文档正文或不必要个人数据

## UX 状态

- 本卡不新增前端页面；API 响应必须足以让未来 UI 展示需求分量、面积缺口、规则版本、计划状态和批准证据

## 可观测性

- 样品需求按 SUFFICIENT/INSUFFICIENT/UNKNOWN 的计数
- CuttingPlan 创建/批准计数和稳定原因码失败计数
- 结构化日志包含 correlationId、对象稳定 ID/版本和 ruleSetVersion，不记录敏感正文

## 测试场景

| ID | 类型 | Given | When | Then |
|---|---|---|---|---|
| TC-TEX-004-01 | positive | 完整版本输入、样品面积充足、有效 CuttingPlan 和授权批准人 | 计算需求、创建计划并批准 | SUFFICIENT；APPROVED 计划固定需求版本/哈希/规则集；状态端口 ALLOWED |
| TC-TEX-004-02 | boundary | 三个平行加复测预留和留样，可用面积少于需求 | 计算并尝试批准计划 | INSUFFICIENT 缺口按款色部件部位并列方向/项目；批准拒绝；补样/范围变更 Outbox 证据存在 |
| TC-TEX-004-03 | negative | 同一裁片被不同互斥破坏组或多条破坏性行共享 | 计算样品需求 | TEX.EXCLUSIVE_SHARE_REJECTED；无半完成需求或计划 |
| TC-TEX-004-04 | negative | 未知规则集或未知方向 | 计算、创建或查询状态 | UNKNOWN 或稳定错误码；不可批准；状态端口不返回 ALLOWED |
| TC-TEX-004-05 | boundary | 尺寸非正、距布边负数或生成试样数与计划数不一致 | 创建计划 | TEX.VALIDATION_FAILED；无业务事实 |
| TC-TEX-004-06 | permission | 具备 manage 但不具备 textile.cutting-plan.approve 的行为人 | 批准计划 | TEX.NOT_AUTHORIZED；批准事实为零；失败尝试留痕 |
| TC-TEX-004-07 | concurrency | 同一 cuttingPlanId 两个请求使用相同 expectedCurrentVersion | 并发追加 | 恰一个成功；另一方 TEX.EXPECTED_VERSION_CONFLICT；版本连续 |
| TC-TEX-004-08 | audit | 注入 audit_intent 或 outbox 写入失败 | 创建需求、计划或批准 | 业务事实与同事务证据全部回滚；独立失败尝试恰一条 |
| TC-TEX-004-09 | recovery | 首次请求在提交前失败且 correlationId 保持不变 | 以当前 expectedCurrentVersion 重试 | 至多一个业务版本；无重复批准或重复 Outbox；原失败证据保留 |
| TC-TEX-004-10 | regression | 已保存需求、计划和批准 | 直接 UPDATE 或 DELETE | 数据库拒绝；原事实可重建且哈希不变 |

## 明确非目标

- 不生产化 OPS-TEXTILE-004 调湿/洗涤及超差工作流
- 不实现 OPS-TEXTILE-005 CoverageDecision 或代表色默认规则
- 不访问 Receiving、Scope、Quantity、Allocation 或其他模块私表
- 不定义方法尺寸、可共享关系、复测预留、留样或自批的业务默认值
- 不新增前端页面
- 不创建 Release、Seal、tag、GitHub Release、部署或执行生产迁移

## 允许修改路径

- `spec/decisions/OD-036__v1.0.0.json`
- `spec/requirements/BUS-TEX-006__v1.0.0.json`
- `spec/requirements/BUS-TEX-007__v1.0.0.json`
- `spec/requirements/BUS-TEX-008__v1.0.0.json`
- `spec/acceptance/AC-TEXTILE-004__v1.0.0.json`
- `spec/stories/ATC-TEX-004__v1.0.0.json`
- `spec/baselines/dev-028-textile-runtime-baseline.lock.json`
- `spec/baselines/dev-028-textile-runtime-baseline-final.lock.json`
- `generated/spec/**`
- `.planning/2026-07-28-dev-028-textile-test-unit/**`
- `.planning/.active_plan`
- `OpenLIMS.slnx`
- `contracts/textile/**`
- `src/modules/textile/**`
- `src/host/api/**`
- `src/host/worker/**`
- `tests/unit/textile/**`
- `tests/contract/textile/**`
- `tests/integration/textile/**`
- `tests/architecture/**`
- `tests/test_repository_contract.py`
- `docs/domain/textile/**`
- `scripts/verify.ps1`
- `scripts/verify.sh`
- `contracts/**/packages.lock.json`
- `src/modules/**/packages.lock.json`
- `src/host/**/packages.lock.json`
- `tests/**/packages.lock.json`

## 验证命令

- `python -m tools.specgen ready --story ATC-TEX-004@1.0.0`
- `pwsh -File scripts/verify.ps1 -Profile task -Module textile`
- `pwsh -File scripts/verify.ps1 -Profile architecture`
- `pwsh -File scripts/verify.ps1 -Profile contracts`
- `python -m tools.specgen check`

## 完成定义

- OD-036@1.0.0、BUS-TEX-006/007/008@1.0.0、AC-TEXTILE-004@1.0.0 和本 Story 均为 approved，ready 返回 READY 后才编码
- 追加最终不可覆盖的 dev-028-textile-runtime-baseline-final requirements snapshot；旧 R1 基线与中间评审 snapshot 均不覆盖
- 正向、反向、边界、权限、并发、恢复、审计/Outbox 和追加式约束均有自动测试
- 样品不足、互斥共享和 UNKNOWN 在批准前失败关闭，缺口与补样/范围变更证据可追溯
- 样品需求、CuttingPlan、批准和公共状态端口固定精确版本与规则集
- 模块只使用平台公共端口，不访问其他模块私表，不修改已发布迁移
- 全仓门禁通过、二次 generate written=0、所有改动位于 approved Story allowed_paths

## AI 执行约束

- 不得修改本文件；它由结构化规格生成。
- 不得把待决策项自行解释为默认业务规则。
- 不得访问其他模块私有表；必须使用批准的端口或事件契约。
- 若前置决策、依赖或测试夹具缺失，应停止实现并报告阻塞，不得猜测。

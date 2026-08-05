<!-- GENERATED FILE — DO NOT EDIT.
Generator: openlims-specgen@0.1.0
Sources: ATC-WEB-004@1.0.0
Edit files under spec/ and run `python -m tools.specgen generate`.
-->

# ATC-WEB-004：实施 Textile Web 工作台

## 元数据

| 字段 | 值 |
|---|---|
| 规格版本 | `1.0.0` |
| 评审状态 | `approved` |
| 目标发布 | `REL-R1-RECEIVING-PILOT@1.0.0` |
| Epic | `EP-EXECUTION` |
| Feature | `FEAT-TEXTILE-WORKBENCH` |
| 开发就绪度 | `ready` |
| 变更级别 | `major` |
| 负责人角色 | 实验室技术负责人, 纺织行业包负责人, 质量负责人, Web应用负责人, QA负责人 |
| 影响模块 | web, textile, sample-requirement, cutting-plan, operator-workbench, accessibility, automated-test |
| 来源 | PRD-MAIN#OPS-TEXTILE-001, PRD-MAIN#OPS-TEXTILE-002, PRD-MAIN#OPS-TEXTILE-003, PRD-MAIN#AC-TEXTILE-001 |
| 固定依赖 | ATC-PLT-003@1.0.0, ATC-TEX-004@1.0.0, ATC-WEB-003@1.0.0, OD-002@1.0.0, OD-036@1.0.0, BUS-TEX-001@1.0.0, BUS-TEX-002@1.0.0, BUS-TEX-003@1.0.0, BUS-TEX-006@1.0.0, BUS-TEX-007@1.0.0, BUS-TEX-008@1.0.0, AC-TEXTILE-004@1.0.0, SEC-AUTH-001@1.0.0, NFR-ARCH-001@2.0.0 |
| 规格指纹 | `fe9bdf7aec5ea3648984b7b1f1f0f8ee66839378e7be666b0f3d2fd062af9bd3` |

## 业务结果

纺织技术人员可从稳定 Web 入口提交完整版本化需求，查看 SUFFICIENT、INSUFFICIENT 或 UNKNOWN 的试样分量和面积缺口，创建绑定需求版本/哈希的 CuttingPlan，并由具备明确批准能力的人员冻结计划，不再直接调用 API。

## 主要参与者

具有对象范围和 textile.sample-requirement.manage 或 textile.cutting-plan.approve 能力的纺织技术人员与批准人员

## 触发条件

已登录操作员进入 Textile 工作台，计算需求、创建/批准 CuttingPlan 或读取指定计划版本

## 前置条件

- ATC-TEX-004@1.0.0 已批准、READY 且运行时已交付
- 用户已通过平台 OIDC 登录，API 基址来自受保护运行配置
- UI 只提交业务字段、稳定 ID、精确版本、输入哈希和固定规则集，不提交可信组织、行为人、批准人或审计身份

## 正常路径

- 提交完整需求行与可用面料，保存不可变计算版本并展示试样分量、面积缺口、规则集和输入哈希
- 以 SUFFICIENT 需求的精确 ID、版本和输入哈希创建 DRAFT CuttingPlan
- 具备批准能力的人员以路径版本、expectedCurrentVersion、输入哈希和固定规则集批准计划
- 按稳定 cuttingPlanId 与正整数精确版本读取计划、需求、缺口和批准证据
- 所有成功只在收到服务端成功响应后呈现

## 失败路径

- 未登录或会话过期 → 引导登录且不发送业务请求
- 无能力或服务端 401/403/404 → 安全呈现，不泄露跨范围对象存在性
- INSUFFICIENT、UNKNOWN、未知方向、互斥共享或结构错误 → 显示稳定错误/原因并失败关闭
- 版本冲突、哈希不匹配或批准门禁未满足 → 保留服务端事实和安全输入，不伪造 APPROVED
- 网络或 5xx → 不自动重复写操作，允许用户显式重试

## 领域不变量

- Web 不推导最新版本、需求充分性、输入哈希、批准资格或规则默认值，不把 UNKNOWN 映射为允许
- 需求、计划和批准使用稳定 ID；已有对象/引用/路径版本为精确正整数，首次 expectedCurrentVersion 可为 0；规则集固定为 TEXTILE-SAMPLE-REQUIREMENT@1.0.0
- 客户端不得提交 organizationGroupId、actorId、approvedBy 或授权决定
- INSUFFICIENT 或 UNKNOWN 不显示为可批准结果；服务器仍为最终权威
- 写操作只在成功响应后更新详情，失败保留可安全重试的非敏感输入
- UI 只消费既有公共 HTTP 契约，不访问其他模块私表或修改 Textile 后端语义
- 既有所有 Web 路由和导航保持兼容

## 数据契约

```json
{
  "cuttingPlan": [
    "cuttingPlanId/version",
    "sampleRequirementId/version/inputHash",
    "ruleSetVersion",
    "sourceItem@version",
    "samplingPosition/direction",
    "lengthMm/widthMm/plannedCount",
    "minDistanceFromSelvedgeMm",
    "templateVersion/operatorId",
    "generatedSpecimenIds[]",
    "state",
    "approval"
  ],
  "problem": [
    "title",
    "detail",
    "status",
    "errorCode",
    "correlationId",
    "nextAction"
  ],
  "sampleRequirement": [
    "requirementId/version",
    "expectedCurrentVersion",
    "objectScope",
    "calculation.ruleSetVersion",
    "demandLines[]",
    "availableFabrics[]",
    "decision/reasonCodes",
    "specimenPlans/gaps",
    "inputHash",
    "createdBy/At"
  ]
}
```

## API / 命令契约

```json
{
  "operations": [
    "POST /api/v1/textile/sample-requirements",
    "POST /api/v1/textile/cutting-plans",
    "POST /api/v1/textile/cutting-plans/{id}/versions/{version}/approval",
    "GET /api/v1/textile/cutting-plans/{id}/versions/{version}"
  ]
}
```

## 状态转换

- UI 只驱动既有追加式 SampleRequirement 和 DRAFT → APPROVED CuttingPlan 状态机
- 本地 loading/success/error 是交互状态，不是业务事实
- 刷新和查询始终按服务器返回的指定版本重建

## 权限与职责分离

- 计算、创建与查询使用 textile.sample-requirement.manage；批准额外使用 textile.cutting-plan.approve
- 按钮能力提示只改善体验，服务器 401/403 始终为最终权威
- 无权读取与对象不存在采用相同安全呈现

## 审计要求

- UI 不直接写审计；写请求由 Textile 后端在业务事务中记录审计与 Outbox
- 错误页面显示 correlationId，浏览器不记录令牌、完整客户正文或可信身份

## UX 状态

- 提供 empty、ready、submitting、success、insufficient/unknown、forbidden、not-found 和 retryable-error 状态
- 深层版本化输入使用带示例和错误定位的结构化 JSON 编辑器
- 导航显示 Textile，稳定 URL 支持深链接
- 详情同时显示版本、规则集、输入哈希、决定、原因、缺口、计划状态与批准证据
- 键盘和窄屏可完成所有操作

## 可观测性

- 客户端错误仅记录 operationId、HTTP 状态、errorCode 和 correlationId，不记录请求正文或令牌
- 测试覆盖 4 个客户端操作、描述符、路由与核心交互

## 测试场景

| ID | 类型 | Given | When | Then |
|---|---|---|---|---|
| TC-WEB-004-01 | positive | 已认证且需求 SUFFICIENT | 计算需求、创建计划、批准并查询 | 4 个操作成功；精确版本/哈希传递；APPROVED 只来自响应 |
| TC-WEB-004-02 | negative | 服务端返回面积不足或未知 | 展示并尝试后续操作 | 原因和缺口可见；不伪造可批准状态 |
| TC-WEB-004-03 | boundary | 版本非正、未知方向、尺寸非正或需求数组为空 | 提交 | 客户端阻止明显无效输入；不发送请求 |
| TC-WEB-004-04 | permission | 仅有 manage 或匿名 | 尝试批准或进入页面 | 批准禁用或引导登录；服务端拒绝不被覆盖 |
| TC-WEB-004-05 | recovery | 首次请求网络失败 | 用户显式重试 | 输入保留；不自动重复写入；成功后才更新 |
| TC-WEB-004-06 | audit | API 返回 errorCode/correlationId | 页面呈现 | 关联信息可见；令牌和可信身份不可见 |
| TC-WEB-004-07 | regression | 既有 Receiving、实验室和业务工作台 | 注册 Textile | 既有路由测试通过；无重复 route/navigation ID |

## 明确非目标

- 不修改 Textile 后端契约、数据库、状态机、规则、权限、审计、Outbox 或迁移
- 不实现调湿/洗涤超差、CoverageDecision、代表色、默认共享或自批规则
- 不新增前端本地业务事实库、离线写队列或运行时 feature discovery
- 不创建 OD、ADR、Seal、Release、tag、部署或执行生产迁移

## 允许修改路径

- `spec/stories/ATC-WEB-004__v1.0.0.json`
- `generated/spec/**`
- `.planning/2026-08-05-business-web-workbenches/**`
- `.planning/.active_plan`
- `apps/web/src/**`
- `tests/test_repository_contract.py`

## 验证命令

- `python -m tools.specgen ready --story ATC-WEB-004@1.0.0`
- `pnpm -C apps/web test:unit`
- `pnpm -C apps/web typecheck`
- `pnpm -C apps/web lint`
- `pnpm -C apps/web build`
- `python -m tools.specgen check`

## 完成定义

- ATC-WEB-004@1.0.0 与全部精确依赖均 approved 且 READY 后才修改产品代码
- Textile 在 Web registry 显式注册且不与既有路由冲突
- 4 个 HTTP 操作及错误、边界、权限、恢复、审计安全和回归状态有自动测试
- UI 不提交可信身份、不推导版本/哈希/规则/充分性且不新增后端语义
- 前端 test、typecheck、lint、build 与全仓门禁通过，二次 generate written=0
- 所有改动位于本 Story allowed_paths

## AI 执行约束

- 不得修改本文件；它由结构化规格生成。
- 不得把待决策项自行解释为默认业务规则。
- 不得访问其他模块私有表；必须使用批准的端口或事件契约。
- 若前置决策、依赖或测试夹具缺失，应停止实现并报告阻塞，不得猜测。

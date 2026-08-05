<!-- GENERATED FILE — DO NOT EDIT.
Generator: openlims-specgen@0.1.0
Sources: ATC-WEB-005@1.0.0
Edit files under spec/ and run `python -m tools.specgen generate`.
-->

# ATC-WEB-005：实施 Toy 全流程 Web 工作台

## 元数据

| 字段 | 值 |
|---|---|
| 规格版本 | `1.0.0` |
| 评审状态 | `approved` |
| 目标发布 | `REL-R1-RECEIVING-PILOT@1.0.0` |
| Epic | `EP-EXECUTION` |
| Feature | `FEAT-TOY-FULL-FLOW-WORKBENCH` |
| 开发就绪度 | `ready` |
| 变更级别 | `major` |
| 负责人角色 | 玩具行业包负责人, 实验室技术负责人, 质量负责人, 授权签字人, Web应用负责人, QA负责人 |
| 影响模块 | web, toy, age-grade, accessibility, test-unit, sample-requirement, label-review, conclusion, operator-workbench, accessibility-ui, automated-test |
| 来源 | PRD-MAIN#OPS-TOY-001, PRD-MAIN#OPS-TOY-002, PRD-MAIN#OPS-TOY-003, PRD-MAIN#OPS-TOY-004, PRD-MAIN#OPS-TOY-005, PRD-MAIN#OPS-TOY-006, PRD-MAIN#OPS-TOY-007, PRD-MAIN#AC-TOY-001, PRD-MAIN#AC-TOY-002 |
| 固定依赖 | ATC-PLT-003@1.0.0, ATC-TOY-001@1.0.0, ATC-TOY-002@1.0.0, ATC-TOY-003@1.0.0, ATC-TOY-004@1.0.0, ATC-TOY-005@1.0.0, ATC-WEB-004@1.0.0, OD-002@1.0.0, OD-034@1.0.0, BUS-TOY-001@1.0.0, BUS-TOY-002@1.0.0, BUS-TOY-003@1.0.0, BUS-TOY-004@1.0.0, BUS-TOY-005@1.0.0, BUS-TOY-006@1.0.0, AC-TOY-001@1.0.0, AC-TOY-002@1.0.0, AC-TOY-003@1.0.0, AC-TOY-004@1.0.0, SEC-AUTH-001@1.0.0, NFR-ARCH-001@2.0.0 |
| 规格指纹 | `85f847e4431215f274104cde879728edc9e2558be67967f0dc1bfd56b249aeb6` |

## 业务结果

玩具技术人员可在稳定、认证、错误可恢复的 Web 工作台中完成产品年龄与可及性链、TestUnit 与样品需求链、标签工件和审核链，并由具备相应结论批准能力的人员创建两级固定结论；每一步展示精确版本、证据、阻断和未覆盖项。

## 主要参与者

具有相应对象范围及 toy.manage、toy.sample-demand.approve、toy.label.manage、toy.label.review、toy.conclusion.approve-item 或 toy.conclusion.approve-scope 能力的技术人员、审核人和批准人

## 触发条件

已登录操作员进入 Toy 产品、TestUnit、LabelReview 或结论工作台，执行任一批准创建、追加、决定、查询或状态操作

## 前置条件

- ATC-TOY-001/002/003/005@1.0.0 已批准、READY 且运行时已交付
- 用户已通过平台 OIDC 登录，API 基址来自受保护运行配置
- UI 只提交业务字段、稳定 ID、精确版本、规则集、证据引用和批准契约明确要求的重认证/签署输入，不提交可信组织、会话行为人、权限或审计身份

## 正常路径

- 记录客户年龄声明和实验室年龄分级业务决定，冻结指定决定版本，按阶段记录可及性并解决范围重评触发，读取产品概览
- 以产品、年龄决定、可及性、范围和规则精确版本创建 TestUnit 计划，批准需求，调用 Quantity/Allocation 公共门禁并读取计划
- 创建标签工件及其后继版本，创建绑定产品/年龄/市场/语言/范围的审核，记录批准或拒绝并查询精确状态
- 创建 ITEM_CONFORMITY 或 TESTED_SCOPE_CONFORMITY 固定结论，按稳定结论 ID 查询，或按产品精确版本列出结论
- 所有写成功只由服务器响应驱动，所有读取展示服务端版本、状态、原因和证据

## 失败路径

- 未登录、无能力、对象不可访问 → 安全提示或引导登录，不泄露对象存在性
- 年龄决定冻结、可及性重评待处理、版本冲突或规则未知 → 显示稳定错误并失败关闭
- 样品需求 UNKNOWN/未批准、破坏性 TestUnit 冲突或下游 Quantity/Allocation 阻断 → 不伪造分配
- 标签影响 UNKNOWN、审核无效/拒绝或版本失效 → 不显示为 VALID
- 结论证据不完整/UNKNOWN、签署绑定失败、SoD 冲突、虚构整件结论或自选措辞 → 不产生结论成功
- 网络或 5xx → 保留非敏感输入，只允许用户显式重试

## 领域不变量

- Web 不推导最新版本、规则、年龄结论、可及性状态、样品需求、标签有效性、覆盖范围、批准人或结论措辞，不把 UNKNOWN 映射为允许
- 四条链均携带稳定 ID、精确版本和固定规则集；初次 expectedCurrentVersion 可按批准契约为 0
- 客户端不提交 organizationGroupId、actorId、reviewedBy、createdBy 或服务端授权决定；approvedBy/signatoryId 类字段只在公共契约明确作为业务引用时提交，不替代会话身份
- ITEM_CONFORMITY 与 TESTED_SCOPE_CONFORMITY 是仅有的结论层级；整件全面合规和 customStatement 始终禁止
- TESTED_SCOPE_CONFORMITY 必须显式提供 TestUnit 证据、未覆盖项、重认证引用、签署意图和服务端绑定内容哈希
- 写操作只有成功响应后才更新结果，失败保留可安全重试输入且不自动重复
- UI 只消费既有公共 HTTP 契约，不访问模块私表或修改 Toy 后端语义
- 所有既有 Web 路由和导航保持兼容

## 数据契约

```json
{
  "conclusion": [
    "conclusionId/level/fixed statement",
    "approvedBy/At",
    "version/signatureRef/contentHash",
    "coveredHazardDomains",
    "uncoveredScopes",
    "externalReferences"
  ],
  "labelReview": [
    "artifactId/versions/contentHash/imageEvidence",
    "reviewId/versions",
    "product/age/artifact exact versions",
    "scopeRefs/impactRule",
    "decision/invalidation/impact",
    "status/reasonCodes"
  ],
  "problem": [
    "title",
    "detail",
    "status",
    "errorCode",
    "correlationId",
    "nextAction"
  ],
  "product": [
    "productId/version",
    "age declarations",
    "age-grade decisions/freeze",
    "accessibility assessments",
    "reassessment triggers",
    "accessibilityStatus",
    "ruleSetVersion"
  ],
  "testUnit": [
    "planId/productId/productVersion/planVersion",
    "age/accessibility/scope exact versions",
    "testUnits/sequenceSteps",
    "requirement components/totals/decision/inputHash",
    "technicalApproval",
    "downstreamDecisions"
  ]
}
```

## API / 命令契约

```json
{
  "conclusion": [
    "POST /api/v1/toy/conclusions/item-conformity",
    "POST /api/v1/toy/conclusions/tested-scope-conformity",
    "GET /api/v1/toy/conclusions/{id}",
    "GET /api/v1/toy/conclusions?productRef={ref}&productVersion={version}"
  ],
  "labelReview": [
    "POST /api/v1/toy/products/{id}/label-artifacts",
    "POST /api/v1/toy/products/{id}/label-artifacts/{artifactId}/versions",
    "POST /api/v1/toy/products/{id}/label-artifacts/{artifactId}/reviews",
    "POST /api/v1/toy/products/{id}/label-reviews/{reviewId}/decision",
    "GET /api/v1/toy/products/{id}/label-reviews/status"
  ],
  "product": [
    "POST /api/v1/toy/products/{id}/age-declarations",
    "POST /api/v1/toy/products/{id}/age-grade-decisions",
    "POST /api/v1/toy/products/{id}/age-grade-decisions/{versionNumber}/freeze",
    "POST /api/v1/toy/products/{id}/accessibility-assessments",
    "POST /api/v1/toy/products/{id}/reassessment-triggers/{triggerId}/resolution",
    "GET /api/v1/toy/products/{id}/overview"
  ],
  "testUnit": [
    "POST /api/v1/toy/products/{id}/test-unit-plans",
    "POST /api/v1/toy/products/{id}/test-unit-plans/{planVersion}/approval",
    "POST /api/v1/toy/products/{id}/test-unit-plans/{planVersion}/allocations",
    "GET /api/v1/toy/products/{id}/test-unit-plans/{planVersion}"
  ]
}
```

## 状态转换

- UI 只驱动四个既有 Toy 状态机，不新增或重解释业务转换
- 本地 loading/success/error 是交互状态，不是业务事实
- 冻结、批准、分配、审核决定和结论均由服务器成功响应确认；刷新后从服务器重建

## 权限与职责分离

- 产品链使用 toy.manage；需求批准额外使用 toy.sample-demand.approve
- 标签工件使用 toy.label.manage，审核与决定使用 toy.label.review
- 两级结论分别使用 toy.conclusion.approve-item 和 toy.conclusion.approve-scope
- 按钮能力提示只改善体验，服务器 401/403 始终为最终权威
- 无权读取与对象不存在采用相同安全呈现

## 审计要求

- UI 不直接写审计；后端在相应业务事务中记录审计、Outbox 和签署证据
- 错误页面显示 correlationId；浏览器不记录令牌、完整客户正文、图像内容、签署 Secret 或可信身份

## UX 状态

- 四个页面均提供 empty、ready、submitting、success、blocked/unknown/invalidated、forbidden、not-found 和 retryable-error 状态
- 深层版本化输入使用带示例和错误定位的结构化 JSON 编辑器
- 导航明确区分 Toy 产品、TestUnit、标签审核和结论并支持稳定深链接
- 详情显示 ID、版本、规则集、状态、原因、证据、未覆盖项和安全下一步
- 所有操作支持键盘和窄屏，不只使用颜色表达

## 可观测性

- 客户端错误仅记录 operationId、HTTP 状态、errorCode 和 correlationId，不记录请求正文/令牌
- 测试覆盖 19 个客户端操作、四个路由/导航和四条核心交互链

## 测试场景

| ID | 类型 | Given | When | Then |
|---|---|---|---|---|
| TC-WEB-005-01 | positive | 已认证且 19 个操作返回成功 | 完成产品、TestUnit、标签审核和两级结论操作 | 四个页面可用；精确版本/证据可见；成功只由响应驱动 |
| TC-WEB-005-02 | negative | 年龄、样品需求、下游分配、标签影响或结论证据任一 UNKNOWN | 查看或尝试后续动作 | 原因可见；不伪造允许、VALID、APPROVED 或结论 |
| TC-WEB-005-03 | boundary | 版本非法、阶段/范围/类型非法、证据数组为空或哈希无效 | 提交 | 客户端阻止明显无效输入；不发送请求 |
| TC-WEB-005-04 | permission | 仅有部分 Toy capability 或匿名 | 执行不同组操作 | 缺失能力动作禁用或引导登录；服务端拒绝不被覆盖 |
| TC-WEB-005-05 | concurrency | 服务端返回 expected version conflict | 用户提交旧版本 | 冲突和 correlationId 可见；不自动改用最新版重试 |
| TC-WEB-005-06 | recovery | 首次请求网络失败 | 用户显式重试 | 输入保留；不自动重复写入；成功后才更新 |
| TC-WEB-005-07 | audit | 结论 API 返回 errorCode/correlationId | 页面呈现 | 关联信息可见；令牌、Secret、图像内容和可信身份不可见 |
| TC-WEB-005-08 | regression | 现有 Receiving、实验室、Billing/Labeling、Textile 路由 | 注册 Toy 四路由 | 既有测试通过；无重复 route/navigation ID |

## 明确非目标

- 不修改 Toy 后端契约、数据库、状态机、规则、权限、审计、签署端口或迁移
- 不实现整件全面合规结论、自选结论措辞、隐式覆盖、自动审批或自动分配
- 不上传标签图像正文；只提交既有对象存储引用和哈希
- 不新增本地业务事实库、离线写队列或运行时 feature discovery
- 不创建 OD、ADR、Seal、Release、tag、部署或执行生产迁移

## 允许修改路径

- `spec/stories/ATC-WEB-005__v1.0.0.json`
- `generated/spec/**`
- `.planning/2026-08-05-business-web-workbenches/**`
- `.planning/.active_plan`
- `apps/web/src/**`
- `tests/test_repository_contract.py`

## 验证命令

- `python -m tools.specgen ready --story ATC-WEB-005@1.0.0`
- `pnpm -C apps/web test:unit`
- `pnpm -C apps/web typecheck`
- `pnpm -C apps/web lint`
- `pnpm -C apps/web build`
- `python -m tools.specgen check`

## 完成定义

- ATC-WEB-005@1.0.0 与全部精确依赖均 approved 且 READY 后才修改产品代码
- Toy 四个工作台在 Web registry 显式注册且不与既有路由冲突
- 19 个 HTTP 操作及错误、边界、权限、并发、恢复、审计安全和回归状态有自动测试
- UI 不提交可信会话身份、不推导版本/规则/状态/覆盖/措辞且不新增后端语义
- 前端 test、typecheck、lint、build 与全仓门禁通过，二次 generate written=0
- 所有改动位于本 Story allowed_paths

## AI 执行约束

- 不得修改本文件；它由结构化规格生成。
- 不得把待决策项自行解释为默认业务规则。
- 不得访问其他模块私有表；必须使用批准的端口或事件契约。
- 若前置决策、依赖或测试夹具缺失，应停止实现并报告阻塞，不得猜测。

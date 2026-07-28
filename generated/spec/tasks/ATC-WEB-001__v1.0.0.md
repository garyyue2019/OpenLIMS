<!-- GENERATED FILE — DO NOT EDIT.
Generator: openlims-specgen@0.1.0
Sources: ATC-WEB-001@1.0.0
Edit files under spec/ and run `python -m tools.specgen generate`.
-->

# ATC-WEB-001：实施 Scope、Quantity、Allocation 与 Batch 实验室工作台

## 元数据

| 字段 | 值 |
|---|---|
| 规格版本 | `1.0.0` |
| 评审状态 | `approved` |
| 目标发布 | `REL-R1-RECEIVING-PILOT@1.0.0` |
| Epic | `EP-EXECUTION` |
| Feature | `FEAT-LAB-WORKBENCH-CORE-FLOW` |
| 开发就绪度 | `ready` |
| 变更级别 | `major` |
| 负责人角色 | 实验室运营负责人, 实验室技术负责人, Web应用负责人, QA负责人 |
| 影响模块 | web, scope, quantity, allocation, batch, operator-workbench, accessibility, automated-test |
| 来源 | PRD-MAIN#BUS-SCOPE-001, PRD-MAIN#BUS-SCOPE-002, PRD-MAIN#BUS-SCOPE-003, PRD-MAIN#AC-SCOPE-001, PRD-MAIN#OPS-QTY-001, PRD-MAIN#OPS-QTY-002, PRD-MAIN#OPS-QTY-003, PRD-MAIN#OPS-QTY-004, PRD-MAIN#AC-QTY-001, PRD-MAIN#OPS-ALLOC-001, PRD-MAIN#OPS-ALLOC-002, PRD-MAIN#OPS-ALLOC-003, PRD-MAIN#OPS-ALLOC-004, PRD-MAIN#AC-ELEC-003, PRD-MAIN#OPS-BATCH-001, PRD-MAIN#OPS-BATCH-002, PRD-MAIN#OPS-BATCH-003, PRD-MAIN#AC-BATCH-001 |
| 固定依赖 | ATC-PLT-003@1.0.0, ATC-SCP-001@1.0.0, ATC-QTY-001@1.0.0, ATC-ALLOC-001@1.0.0, ATC-BATCH-001@1.0.0, OD-002@1.0.0, AC-SCOPE-001@1.0.0, AC-QTY-001@1.0.0, AC-ELEC-003@1.0.0, AC-BATCH-001@1.0.0, SEC-AUTH-001@1.0.0, NFR-ARCH-001@2.0.0 |
| 规格指纹 | `8e37afc7bc27fbcd69cbc2abc5e171a47f8a2cc6d9714f3c0c5ae2d84aef323e` |

## 业务结果

实验室操作员能够在一个经过身份认证、可键盘操作且错误可恢复的工作台中完成范围矩阵、数量账户、样品分配和批次的创建与查询；每一步展示服务器返回的精确对象版本、资格、可用量、状态和阻断原因，不再依赖直接调用 API 或阅读数据库。

## 主要参与者

创建检测范围、维护样品数量、请求样品分配并组织实验室批次的授权操作员和技术负责人

## 触发条件

已登录操作员从导航进入实验室工作台，创建或查询范围矩阵、数量账户、样品分配或批次

## 前置条件

- ATC-SCP-001@1.0.0、ATC-QTY-001@1.0.0、ATC-ALLOC-001@1.0.0 与 ATC-BATCH-001@1.0.0 已批准、READY 且运行时已交付
- 用户已通过平台 OIDC 登录，API 基址来自受保护的运行配置
- UI 仅提交业务字段、目标对象范围、稳定 ID、精确版本和 expectedCurrentVersion，不提交可信组织集团、行为人或授权决定

## 正常路径

- 创建范围矩阵并追加批准版本，工作台展示矩阵版本和生产资格
- 为收样对象创建数量账户并追加数量流水，工作台展示当前账户版本和可用量
- 使用精确收样放行、范围资格和数量版本请求样品分配，工作台展示分配决定和稳定原因码
- 使用允许的分配版本创建类型化批次并追加成员/证据，工作台展示批次状态、成员和冻结信息
- 各步骤可通过 URL 深链重新打开，重新加载后从服务器恢复，不依赖浏览器内临时状态作为事实来源

## 失败路径

- 未登录或会话过期 → 返回登录入口并保留安全 return URL，不发送业务请求
- 服务器返回 401/403 → 显示无权操作且不把本地表单标记为成功
- 服务器返回 RFC 9457 Problem Details → 显示稳定 errorCode、correlationId、问题说明和安全下一步
- UNKNOWN、BLOCKED、余额不足、版本冲突或批次冻结 → 保留服务器状态并禁用不允许的后续动作
- 网络或 5xx 失败 → 保留非敏感表单输入，允许显式重试且不自动重复提交写操作
- 输入缺失、数值非正或版本不是正整数 → 客户端即时提示；服务器仍为最终校验权威

## 领域不变量

- Web 端不得推导最新版、伪造成功状态或把 UNKNOWN 映射为允许
- 所有跨步骤引用都携带稳定 ID 与精确正整数版本
- 可信 organizationGroupId、actorId、权限和审计字段只由服务器上下文提供；客户端可提交目标 legalEntity/laboratory/customer/order/category，服务器必须按可信声明重新授权
- 写操作只有收到成功响应后才更新工作台结果；失败不清空可安全重试的非敏感输入
- UI 不读取任何模块私表，也不引入跨模块后端依赖，只消费现有公共 HTTP 契约
- Receiving 现有页面、路由和行为保持兼容

## 数据契约

```json
{
  "allocation": [
    "allocationId",
    "version",
    "receivedItemRef@version",
    "scopeRef@version",
    "quantityRef@version",
    "requestedAmount",
    "decision",
    "reasonCodes"
  ],
  "batch": [
    "batchId",
    "version",
    "batchType",
    "methodRef@version",
    "members",
    "evidenceRefs",
    "state",
    "freezeReasons"
  ],
  "problem": [
    "title",
    "detail",
    "status",
    "errorCode",
    "correlationId",
    "nextAction"
  ],
  "quantity": [
    "accountId",
    "version",
    "receivedItemRef@version",
    "dimension",
    "entries",
    "balance",
    "available",
    "reasonCodes"
  ],
  "scope": [
    "matrixId",
    "version",
    "expectedCurrentVersion",
    "profileRef@version",
    "rows",
    "state",
    "eligibility",
    "reasonCodes"
  ]
}
```

## API / 命令契约

```json
{
  "allocation": [
    "POST /api/v1/test-object-allocations",
    "POST /api/v1/test-object-allocations/{id}/release",
    "GET /api/v1/test-object-allocations/{id}",
    "GET /api/v1/test-object-allocations/{id}/status"
  ],
  "batch": [
    "POST /api/v1/batches",
    "POST /api/v1/batches/{id}/members",
    "POST /api/v1/batches/{id}/evidence",
    "POST /api/v1/batches/{id}/freeze",
    "GET /api/v1/batches/{id}",
    "GET /api/v1/batches/{id}/status"
  ],
  "quantity": [
    "POST /api/v1/quantity-accounts",
    "POST /api/v1/quantity-accounts/{id}/entries",
    "GET /api/v1/quantity-accounts/{id}",
    "GET /api/v1/quantity-accounts/{id}/availability"
  ],
  "scope": [
    "POST /api/v1/scope-matrices",
    "POST /api/v1/scope-matrices/{id}/versions",
    "GET /api/v1/scope-matrices/{id}/versions/{version}",
    "GET /api/v1/scope-matrices/{id}/production-eligibility"
  ]
}
```

## 状态转换

- UI 只展示并驱动四个既有模块的批准状态机，不新增或重解释任何状态转换
- 本地 submitting/success/error 是交互状态，不是业务事实；刷新后始终以服务器响应重建
- BLOCKED、UNKNOWN、FROZEN 和版本冲突时后续按钮按服务器事实禁用并展示原因

## 权限与职责分离

- 所有路由要求已认证会话；匿名用户被引导至登录
- 按钮级提示可以依据已知能力改善体验，但服务器 401/403 始终为最终权威
- 无权读取与对象不存在使用同一安全错误呈现，不泄露跨组织对象存在性
- UI 不接受或缓存可被当作授权证据的客户端声明

## 审计要求

- UI 不直接写审计；每个写请求由对应后端模块在同事务内记录审计与 Outbox
- 错误视图显示服务器 correlationId 便于支持与审计定位
- 浏览器日志和错误呈现不得包含令牌、Secret、完整客户文档或不必要个人数据

## UX 状态

- 每个模块提供 loading、empty、ready、submitting、success、blocked/unknown、forbidden、not-found 和 retryable-error 状态
- 表单字段具有可见标签、错误关联、键盘提交和焦点回到首个错误
- 导航明确显示 Scope、Quantity、Allocation、Batch，并支持直接 URL 深链
- 对象详情显示 ID、精确版本、状态、规则/原因码和允许的下一步，不只使用颜色表达
- 窄屏下表单和详情纵向排列，关键操作不依赖悬停

## 可观测性

- 客户端错误记录 operationId、HTTP 状态、errorCode 和 correlationId，不记录令牌或请求正文
- 页面可见的 correlationId 与 API Problem Details 一致
- 前端构建和测试覆盖四个 feature descriptor、路由与核心交互

## 测试场景

| ID | 类型 | Given | When | Then |
|---|---|---|---|---|
| TC-WEB-001-01 | positive | 已认证且服务器依次返回范围、数量、分配和批次成功响应 | 操作员提交每一步并打开结果 | 导航和四个页面可用；精确版本传递到下一步；成功只由响应驱动 |
| TC-WEB-001-02 | negative | API 返回稳定 errorCode、correlationId 和 nextAction | 写请求失败 | 显示安全错误详情；保留非敏感输入；不显示成功 |
| TC-WEB-001-03 | boundary | 版本为零/小数或数量非正 | 操作员提交 | 客户端阻止明显无效输入；不发送请求 |
| TC-WEB-001-04 | permission | 会话匿名、过期或 API 返回 403 | 进入路由或执行操作 | 引导登录或显示无权；不泄露对象存在性 |
| TC-WEB-001-05 | recovery | 首次网络请求失败 | 用户确认后重试 | 不自动重复写操作；表单内容保留；成功响应后才更新详情 |
| TC-WEB-001-06 | regression | 现有 Receiving 路由和导航 | 注册四个新 feature | Receiving 路由和测试保持通过；无重复 route/navigation ID |

## 明确非目标

- 不修改 Scope、Quantity、Allocation、Batch 的后端契约、数据库、状态机、权限或业务默认值
- 不实现 Instrument、Result、QC、Report、Toy、Textile 或 AI 页面（后续功能批次）
- 不新增运行时 feature discovery、前端本地业务事实库或离线写队列
- 不创建 OD、ADR、Seal、Release、tag、部署或生产迁移

## 允许修改路径

- `spec/stories/ATC-WEB-001__v1.0.0.json`
- `generated/spec/**`
- `.planning/2026-07-29-lab-workbench-core-flow/**`
- `.planning/.active_plan`
- `apps/web/src/**`
- `tests/test_repository_contract.py`

## 验证命令

- `python -m tools.specgen ready --story ATC-WEB-001@1.0.0`
- `pnpm -C apps/web test:unit`
- `pnpm -C apps/web lint`
- `pnpm -C apps/web build`
- `python -m tools.specgen check`

## 完成定义

- ATC-WEB-001@1.0.0 与全部精确依赖均为 approved 且 READY 后才修改产品代码
- 四个 feature 在 Web registry 显式注册，路由和导航稳定且不与 Receiving 冲突
- 四模块创建/查询主路径、错误、边界、权限和恢复状态有自动测试
- UI 只消费现有 API，不提交可信上下文或新增后端业务语义
- 前端 test、lint、build 与全仓门禁通过，二次 generate written=0
- 所有改动位于本 Story allowed_paths

## AI 执行约束

- 不得修改本文件；它由结构化规格生成。
- 不得把待决策项自行解释为默认业务规则。
- 不得访问其他模块私有表；必须使用批准的端口或事件契约。
- 若前置决策、依赖或测试夹具缺失，应停止实现并报告阻塞，不得猜测。

<!-- GENERATED FILE — DO NOT EDIT.
Generator: openlims-specgen@0.1.0
Sources: ATC-WEB-003@1.0.0
Edit files under spec/ and run `python -m tools.specgen generate`.
-->

# ATC-WEB-003：实施 Billing 与 Labeling Web 工作台

## 元数据

| 字段 | 值 |
|---|---|
| 规格版本 | `1.0.0` |
| 评审状态 | `approved` |
| 目标发布 | `REL-R1-RECEIVING-PILOT@1.0.0` |
| Epic | `EP-BILLING-INTEGRATION` |
| Feature | `FEAT-BILLING-LABELING-WORKBENCH` |
| 开发就绪度 | `ready` |
| 变更级别 | `major` |
| 负责人角色 | 财务负责人, 实验室运营负责人, 收样产品负责人, 质量负责人, Web应用负责人, QA负责人 |
| 影响模块 | web, billing, labeling, operator-workbench, accessibility, automated-test |
| 来源 | PRD-MAIN#FIN-BILL-001, PRD-MAIN#FIN-BILL-002, PRD-MAIN#FIN-BILL-003, PRD-MAIN#FIN-BILL-004, PRD-MAIN#FIN-BILL-005, PRD-MAIN#AC-BILL-001, PRD-MAIN#OPS-RECEIPT-002, PRD-MAIN#OD-031 |
| 固定依赖 | ATC-PLT-003@1.0.0, ATC-BILL-001@1.0.0, ATC-REC-002@2.0.0, ATC-WEB-001@1.0.0, ATC-WEB-002@1.0.0, OD-002@1.0.0, BUS-BILL-001@1.0.0, BUS-BILL-002@1.0.0, BUS-BILL-003@1.0.0, AC-BILL-001@1.0.0, OPS-RECEIPT-002@1.0.0, OD-031@1.0.0, SEC-AUTH-001@1.0.0, NFR-ARCH-001@2.0.0 |
| 规格指纹 | `6581fa2f4d8344db3f30a1e45c09ca55b5d88baf035f5f210157feec78bd7a13` |

## 业务结果

计费操作员可在报告和结果采用后的商业闭环中创建并核对唯一计费证据、追加不可变调整并查看服务端状态；收样和样品操作员可从独立导航入口处理现有对象的打印、任务查询、受控重印和扫码校验，不再依赖刚完成的收样登记页面或直接调用 API。

## 主要参与者

具有对象范围和 billing.record、receiving.label.print、receiving.label.scan 或 receiving.label.reprint 相应能力的计费操作员、收样员、样品管理员和主管

## 触发条件

已登录操作员从导航进入 Billing 或 Labeling 工作台，执行任一批准的创建、调整、查询、重印或扫码操作

## 前置条件

- ATC-BILL-001@1.0.0 与 ATC-REC-002@2.0.0 已批准、READY 且运行时已交付
- 用户已通过平台 OIDC 登录，API 基址来自受保护的运行配置
- UI 只提交业务字段、稳定 ID、精确版本、打印机 ID、扫码载荷和幂等键，不提交可信组织、行为人、权限或审计身份

## 正常路径

- 按精确结果组版本、合同基线版本、收费维度、计费规则和币种版本创建计费证据，展示服务端固定的采用目标、阶段和记录人
- 为现有计费证据追加非零正负调整并展示完整调整链和净额相关状态
- 按稳定计费证据 ID 查询详情和服务端 ALLOWED、BLOCKED 或 UNKNOWN 状态
- 按 CT 或 RI 对象稳定 ID 与精确版本创建标签任务，并按任务 ID 查询发送或校验状态
- 对现有任务填写打印机和非空原因进行受控重印，或提交扫码载荷解析对象与打印校验状态
- 所有成功只在收到服务端成功响应后呈现

## 失败路径

- 未登录或会话过期 → 引导登录且不发送业务请求
- 服务器返回 401/403/404 → 安全呈现无权或不可访问，不泄露跨范围对象存在性
- 服务器返回 Problem Details → 显示稳定 errorCode、correlationId、detail 和安全 nextAction
- 计费资格 BLOCKED/UNKNOWN、重复计费、版本或金额边界失败 → 保留输入和服务端原因，不伪造证据或允许状态
- 标签 UNKNOWN、打印机范围错误、重印原因缺失或超过阈值 → 保留任务事实并禁止普通自动重试
- 网络或 5xx 失败 → 保留非敏感输入，只允许用户显式重试写操作

## 领域不变量

- Web 端不得推导最新版本、计费资格、净额允许状态、打印送达或扫码授权，不把 UNKNOWN 映射为允许
- 可信组织、行为人、权限、记录人和审计身份只由服务端上下文提供
- Billing 金额与调整不得由浏览器改写历史；调整始终追加并引用现有证据
- Labeling 重印沿用服务器对象身份且每次只请求一份；UNKNOWN 不提供普通重试动作
- 写操作只有收到成功响应后才更新工作台结果，失败不清空可安全重试的非敏感输入
- UI 只消费既有公共 HTTP 契约，不修改 Billing、Labeling 或 Receiving 后端语义和私有表
- 现有 Receiving 与两批实验室工作台路由、导航和嵌入式标签操作保持兼容

## 数据契约

```json
{
  "billing": [
    "billingEvidenceId",
    "stage",
    "ruleSetVersion",
    "objectScope",
    "resultGroupId/groupVersion",
    "adoptionTargetId",
    "contractBaseline@version",
    "chargeDimension",
    "billingRuleVersion",
    "amount",
    "currency@version",
    "zeroAmountReason",
    "adjustments",
    "recordedBy/At"
  ],
  "billingStatus": [
    "decision",
    "reasonCodes",
    "billingEvidenceId",
    "stage",
    "amount",
    "adjustmentCount",
    "ruleSetVersion"
  ],
  "labeling": [
    "printJobId",
    "objectType",
    "objectId/objectVersion",
    "businessNumber",
    "templateVersion",
    "printerId",
    "status",
    "isReprint",
    "successfulReprintCount",
    "createdAt",
    "updatedAt"
  ],
  "problem": [
    "title",
    "detail",
    "status",
    "errorCode",
    "correlationId",
    "nextAction"
  ],
  "scan": [
    "objectType",
    "objectId",
    "businessNumber",
    "state",
    "printVerificationStatus",
    "allowedActions"
  ]
}
```

## API / 命令契约

```json
{
  "billing": [
    "POST /api/v1/billing-evidence",
    "POST /api/v1/billing-evidence/{id}/adjustments",
    "GET /api/v1/billing-evidence/{id}",
    "GET /api/v1/billing-evidence/{id}/status"
  ],
  "labeling": [
    "POST /api/v1/label-jobs",
    "GET /api/v1/label-jobs/{printJobId}",
    "POST /api/v1/label-jobs/{printJobId}/reprint",
    "POST /api/v1/scans/resolve"
  ]
}
```

## 状态转换

- UI 只展示并驱动 Billing 与 Labeling 的既有状态机，不新增或重解释业务状态
- 本地 idle/loading/submitting/success/error 是交互状态，不是业务事实
- 详情和状态查询以服务端响应重建；刷新不会从本地缓存恢复权威业务结果

## 权限与职责分离

- 所有路由要求已认证会话；匿名用户被引导登录
- 按钮级 capability 提示只改善体验，服务器 401/403 始终为最终权威
- 无权读取和对象不存在采用相同安全呈现，不泄露对象存在性
- 普通系统管理员不因角色名称自动获得计费、打印、扫码或重印能力

## 审计要求

- UI 不直接写审计；写请求由后端模块在业务事务中记录审计和 Outbox
- 错误视图显示服务器 correlationId 便于支持和审计定位
- 浏览器日志和错误呈现不得包含访问令牌、完整二维码载荷、打印机地址、客户正文或可信身份

## UX 状态

- Billing 和 Labeling 页面分别提供 empty、ready、submitting、success、blocked/unknown、forbidden、not-found 和 retryable-error 状态
- 表单字段具有可见标签、关联错误、键盘提交和清晰的精确版本提示
- 导航明确显示 Billing 和 Labeling 并支持稳定 URL 深链接
- 对象详情显示稳定 ID、精确版本、状态、原因码和允许动作，不只依赖颜色表达
- 窄屏下表单与详情纵向排列，关键动作不依赖悬停

## 可观测性

- 客户端错误仅记录 operationId、HTTP 状态、errorCode 和 correlationId，不记录令牌或请求正文
- 页面可见 correlationId 与 API Problem Details 一致
- 前端构建和测试覆盖两个工作台描述符、路由、8 个客户端操作和核心交互

## 测试场景

| ID | 类型 | Given | When | Then |
|---|---|---|---|---|
| TC-WEB-003-01 | positive | 已认证且服务器返回 8 个操作的成功响应 | 操作员创建、调整、查询计费证据并创建、查询、重印和扫描标签 | 两个导航和页面可用；成功只由响应驱动；精确版本与状态可见 |
| TC-WEB-003-02 | negative | 计费状态 BLOCKED/UNKNOWN 或打印任务 UNKNOWN | 操作员查看结果或尝试后续动作 | 显示服务器原因；不伪造允许或送达；UNKNOWN 不提供普通重试 |
| TC-WEB-003-03 | boundary | 版本非正整数、调整为零、对象类型不支持或重印原因为空 | 操作员提交 | 客户端阻止明显无效输入；不发送请求 |
| TC-WEB-003-04 | permission | 会话匿名、缺 UX capability 或 API 返回 403 | 进入路由或执行操作 | 引导登录或显示只读无权；不泄露对象存在性；不显示成功 |
| TC-WEB-003-05 | recovery | 首次写请求网络失败 | 用户确认后重试 | 不自动重复写操作；非敏感输入保留；成功响应后才更新详情 |
| TC-WEB-003-06 | audit | API 返回 errorCode 和 correlationId | 页面呈现问题 | 显示支持关联信息；不显示令牌、可信身份、完整扫码载荷或打印机地址 |
| TC-WEB-003-07 | regression | Receiving 与两批实验室工作台已注册 | 注册 Billing 与 Labeling | 所有既有路由和测试保持通过；无重复 route 或 navigation ID |

## 明确非目标

- 不修改 Billing、Labeling、Receiving 后端契约、状态机、数据库、权限、Worker 或打印协议
- 不实现发票、应收、收款、核销、收入确认、价格引擎、税务或币种换算
- 不新增浏览器打印、摄像头扫码、原生移动应用、离线写队列或运行时插件
- 不自动轮询或自动重试 UNKNOWN、失败或可能产生副作用的写请求
- 不创建 OD、ADR、Seal、Release、tag、部署或生产迁移

## 允许修改路径

- `spec/stories/ATC-WEB-003__v1.0.0.json`
- `generated/spec/**`
- `.planning/2026-08-05-business-web-workbenches/**`
- `.planning/.active_plan`
- `apps/web/src/**`
- `tests/test_repository_contract.py`

## 验证命令

- `python -m tools.specgen ready --story ATC-WEB-003@1.0.0`
- `pnpm -C apps/web test:unit`
- `pnpm -C apps/web typecheck`
- `pnpm -C apps/web lint`
- `pnpm -C apps/web build`
- `python -m tools.specgen check`

## 完成定义

- ATC-WEB-003@1.0.0 与全部精确依赖均为 approved 且 READY 后才修改产品代码
- Billing 与 Labeling 在 Web registry 显式注册，路由和导航稳定且不与现有功能冲突
- 8 个批准 HTTP 操作及错误、边界、权限、恢复、审计安全和回归状态具有自动测试
- UI 只消费现有 API，不提交可信身份、不伪造业务状态且不新增后端业务语义
- 前端 test、typecheck、lint、build 与全仓门禁通过，二次 generate written=0
- 所有改动位于本 Story allowed_paths

## AI 执行约束

- 不得修改本文件；它由结构化规格生成。
- 不得把待决策项自行解释为默认业务规则。
- 不得访问其他模块私有表；必须使用批准的端口或事件契约。
- 若前置决策、依赖或测试夹具缺失，应停止实现并报告阻塞，不得猜测。

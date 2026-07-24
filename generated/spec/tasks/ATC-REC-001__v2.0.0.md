<!-- GENERATED FILE — DO NOT EDIT.
Generator: openlims-specgen@0.1.0
Sources: ATC-REC-001@2.0.0
Edit files under spec/ and run `python -m tools.specgen generate`.
-->

# ATC-REC-001：登记到货批、包装单元和收到实物

## 元数据

| 字段 | 值 |
|---|---|
| 规格版本 | `2.0.0` |
| 评审状态 | `approved` |
| 目标发布 | `REL-R1-RECEIVING-PILOT@1.0.0` |
| Epic | `EP-RECEIVING` |
| Feature | `FEAT-REC-REGISTRATION` |
| 开发就绪度 | `ready` |
| 变更级别 | `major` |
| 负责人角色 | 收样产品负责人, 收样模块工程负责人, QA负责人 |
| 影响模块 | receiving, authorization, audit, outbox, receiving-ui, automated-test |
| 来源 | PRD-MAIN#OPS-RECEIPT-001, PRD-MAIN#OD-002, PRD-MAIN#OD-009, PRD-MAIN#ORG-STRUCT-001, PRD-MAIN#SEC-AUTH-001, PRD-MAIN#SEC-AUD-001 |
| 固定依赖 | ATC-PLT-003@1.0.0, ED-001@2.0.0, OD-002@1.0.0, ORG-STRUCT-001@1.0.0, ORG-COLLAB-001@1.0.0, SEC-DEPLOY-001@2.0.0, OD-009@1.0.0, OPS-RECEIPT-001@1.0.0, SEC-AUTH-001@1.0.0, AC-SEC-001@1.0.0, SEC-AUD-001@2.0.0, NFR-ARCH-001@2.0.0 |
| 规格指纹 | `5f5d2d7c7e2faf0dd30de7f3da41adc0d9370fd64eff4205b1b3d57f206f9a5a` |

## 业务结果

收样员可以一次登记一笔到货，明确包装单元和逐个完整销售玩具或套装；后续身份、谱系和检测不再依赖含义混乱的单一 Sample 记录。

## 主要参与者

具有当前委托、法人和实验室 receiving.register 权限的收样员

## 触发条件

收样员收到物流包裹，录入物流、包装和完整销售玩具或套装信息并提交登记

## 前置条件

- DEV-001 工程骨架与 DEV-002 模块接入通道已经合并并通过 CI
- 部署已绑定唯一 OrganizationGroup，客户端不能选择或覆盖集团上下文
- 服务委托存在、属于同一集团且处于允许收样状态
- 调用方授权同时覆盖客户、归属法人、收样实验室、服务委托和有效期

## 正常路径

- 服务端从受信上下文解析集团和操作者，并校验客户、法人、实验室、委托和 receiving.register
- 创建 Receipt 到货事实并分配不可复用业务编号
- 按请求创建一个或多个 Container 包装单元
- 按 OD-009 为每个完整销售玩具或套装创建 ReceivedItem，不把包装数量当作实物数量
- 每个 ReceivedItem 由 NONE 进入 REGISTERED，再在同一命令中进入 QUARANTINED
- 返回对象 ID、业务编号、版本和隔离状态
- 同事务写入业务事实、audit_pending 和 Outbox

## 失败路径

- 集团、客户、法人、实验室、委托或 capability 不匹配时默认拒绝且不泄露对象是否存在
- 委托状态不允许收样时返回 SERVICE_ORDER_NOT_RECEIVABLE 且不创建对象
- 相同幂等键和相同载荷返回首次结果且不重复创建
- 相同幂等键和不同载荷返回 IDEMPOTENCY_CONFLICT
- 包装或实物不符合 OD-009 时返回 IDENTITY_GRANULARITY_UNRESOLVED 并整笔失败
- 审计或 Outbox 写入失败时业务事务整体回滚

## 领域不变量

- Receipt、Container、ReceivedItem 身份和业务编号不可互换
- 业务编号在集团和对象类型命名空间内唯一且永不复用，数据库主键使用 UUID/GUID
- 新 ReceivedItem 未完成后续受控放行前必须保持 QUARANTINED
- 所有写入包含 CreatedAt/By、UpdatedAt/By 和期望版本
- 其他模块不得直接访问 receiving 私有表

## 数据契约

```json
{
  "command": "RegisterReceipt",
  "container": [
    "externalLabel",
    "packageType",
    "condition",
    "sealObservation",
    "receivedItems"
  ],
  "output": [
    "receiptId",
    "receiptNumber",
    "containers",
    "receivedItems",
    "aggregateVersion"
  ],
  "receivedItem": [
    "declaredDescription",
    "model",
    "batch",
    "serialNumber",
    "color",
    "packageCondition",
    "sealCondition",
    "itemCondition",
    "quantity",
    "unit"
  ],
  "required": [
    "legalEntityId",
    "laboratoryId",
    "customerId",
    "serviceOrderId",
    "arrivalAt",
    "containers",
    "idempotencyKey"
  ],
  "serverContext": [
    "organizationGroupId",
    "authenticatedSubjectId",
    "grantedOrganizationScopes"
  ]
}
```

## API / 命令契约

```json
{
  "errors": [
    "AUTHORIZATION_DENIED",
    "SERVICE_ORDER_NOT_RECEIVABLE",
    "IDENTITY_GRANULARITY_UNRESOLVED",
    "IDEMPOTENCY_CONFLICT",
    "VALIDATION_FAILED"
  ],
  "idempotencyHeader": "Idempotency-Key",
  "method": "POST",
  "operationId": "registerReceipt",
  "path": "/api/v1/receipts",
  "success": "201 ReceiptRegistrationResult"
}
```

## 状态转换

- ReceivedItem: NONE -> REGISTERED
- ReceivedItem: REGISTERED -> QUARANTINED
- 失败时不得产生可见状态转换

## 权限与职责分离

- 服务端要求 capability=receiving.register
- 授权范围同时匹配部署集团、客户、法人、实验室、委托和有效期
- 系统管理员默认没有业务收样权限
- 客户端不能提交或覆盖 organizationGroupId
- 合法跨实验室收样必须有委托范围内的显式授权并记录各责任主体

## 审计要求

- 记录 RECEIPT_REGISTERED 和 RECEIVED_ITEM_QUARANTINED
- 记录操作者、集团、法人、实验室、客户、委托、对象、correlationId 和幂等键哈希
- 不得记录令牌、Secret、完整附件或未脱敏敏感正文
- 失败阻断记录 ACTION_BLOCKED，但不保存未批准敏感正文

## UX 状态

- 空态：未添加包装和实物时禁止提交
- 编辑态：包装和实物分层显示，多个完整玩具逐个录入
- 提交中：防止重复点击并复用稳定幂等键
- 成功态：展示到货号、包装号、实物号和 QUARANTINED 状态
- 错误态：区分字段错误和业务阻断并保留未提交内容
- 只读态：无 receiving.register 权限时不能提交

## 可观测性

- receipt_registration_total 按结果和实验室聚合，不使用客户名等高基数标签
- receipt_registration_duration_seconds
- 结构化日志包含 correlationId、operationId、organizationGroupId、legalEntityId、laboratoryId 和错误码
- Outbox 积压有独立可检测状态

## 测试场景

| ID | 类型 | Given | When | Then |
|---|---|---|---|---|
| TC-REC-001-01 | positive | 合法委托和授权收样员；一个包装含两个完整玩具 | 提交带幂等键的登记命令 | 创建一笔 Receipt、一个 Container、两个 ReceivedItem；实物均为 QUARANTINED；写入审计和 Outbox |
| TC-REC-001-02 | idempotency | 首次请求已成功 | 相同幂等键和相同载荷重试 | 返回首次结果；对象数量不增加 |
| TC-REC-001-03 | negative | 幂等键已绑定另一载荷 | 复用该键提交不同包装数据 | 返回 IDEMPOTENCY_CONFLICT；没有新增对象 |
| TC-REC-001-04 | security | 用户只获授权法人甲、实验室甲、客户甲和委托甲 | 提交属于任一未授权维度的收样 | 服务端拒绝；不泄露对象是否存在；记录安全审计 |
| TC-REC-001-05 | recovery | 审计或 Outbox 持久化模拟失败 | 提交收样 | 业务对象全部回滚；重试后只产生一套对象 |
| TC-REC-001-06 | security | 部署绑定集团甲；载荷包含集团乙标识 | 提交收样 | 拒绝未知字段；不切换集团上下文；记录安全审计 |
| TC-REC-001-07 | authorization | 用户具有指定委托的跨实验室收样授权 | 提交收样 | 登记成功；权限不扩散到其他委托；审计保留各责任主体 |
| TC-REC-001-08 | boundary | 两个玩具的型号、批次、序列号、颜色、包装、封识或实物状态至少一项不同 | 提交为同一 ReceivedItem | 返回 IDENTITY_GRANULARITY_UNRESOLVED；整笔登记不产生半成品 |
| TC-REC-001-09 | concurrency | 两个请求使用相同幂等键和相同载荷 | 并发提交 | 只创建一套 Receipt、Container 和 ReceivedItem；两个请求返回同一首次结果；审计和 Outbox 不重复 |

## 明确非目标

- 不实现身份评估结论
- 不实现异常审批、条件接收、拒收或解除隔离
- 不实现条码生成与打印
- 不实现零部件、材料、颜色、取样份、制备份、检测任务或报告
- 不实现共享 SaaS 多租户或生产基础设施选型

## 允许修改路径

- `spec/decisions/ED-001__v2.0.0.json`
- `spec/decisions/OD-009__v1.0.0.json`
- `spec/requirements/ORG-STRUCT-001__v1.0.0.json`
- `spec/requirements/ORG-COLLAB-001__v1.0.0.json`
- `spec/requirements/SEC-DEPLOY-001__v2.0.0.json`
- `spec/requirements/OPS-RECEIPT-001__v1.0.0.json`
- `spec/requirements/SEC-AUTH-001__v1.0.0.json`
- `spec/acceptance/AC-SEC-001__v1.0.0.json`
- `spec/requirements/SEC-AUD-001__v2.0.0.json`
- `spec/nfr/NFR-ARCH-001__v2.0.0.json`
- `spec/stories/ATC-REC-001__v2.0.0.json`
- `generated/spec/**`
- `.planning/2026-07-24-dev-003-receipt-registration/**`
- `OpenLIMS.slnx`
- `contracts/receiving/**`
- `src/modules/receiving/**`
- `src/host/api/**`
- `src/host/worker/**`
- `apps/web/src/**`
- `tests/architecture/**`
- `tests/contract/platform/OpenLIMS.Platform.ContractTests/packages.lock.json`
- `tests/unit/receiving/**`
- `tests/integration/platform/OpenLIMS.Platform.IntegrationTests/packages.lock.json`
- `tests/integration/receiving/**`
- `tests/contract/receiving/**`
- `tests/e2e/receiving/**`
- `tests/test_repository_contract.py`
- `docs/domain/receiving/**`
- `scripts/verify.ps1`
- `scripts/verify.sh`
- `.github/workflows/application-ci.yml`

## 验证命令

- `python -m tools.specgen ready --story ATC-REC-001@2.0.0`
- `pwsh -File scripts/verify.ps1 -Profile task -Module receiving`
- `pwsh -File scripts/verify.ps1 -Profile architecture`
- `pwsh -File scripts/verify.ps1 -Profile contracts`
- `corepack pnpm@10.34.5 --dir apps/web lint`
- `corepack pnpm@10.34.5 --dir apps/web typecheck`
- `corepack pnpm@10.34.5 --dir apps/web test:unit`
- `python -m tools.specgen check`

## 完成定义

- 迁移、API、页面、权限、审计和 Outbox 在同一变更集中完成
- 正向、反向、边界、权限、并发、恢复和审计测试全部自动化并通过
- 接口和错误码文档无未声明字段
- 不存在跨模块私表访问或循环依赖
- 可从空数据库登记一笔到货并查看逐个隔离实物和审计证据
- 规格生成、历史验证、追踪和确定性二次生成门禁通过

## AI 执行约束

- 不得修改本文件；它由结构化规格生成。
- 不得把待决策项自行解释为默认业务规则。
- 不得访问其他模块私有表；必须使用批准的端口或事件契约。
- 若前置决策、依赖或测试夹具缺失，应停止实现并报告阻塞，不得猜测。

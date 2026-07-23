<!-- GENERATED FILE — DO NOT EDIT.
Generator: openlims-specgen@0.1.0
Sources: ATC-REC-001@0.1.0
Edit files under spec/ and run `python -m tools.specgen generate`.
-->

# ATC-REC-001：登记到货批、包装单元和收到实物

## 元数据

| 字段 | 值 |
|---|---|
| 规格版本 | `0.1.0` |
| 评审状态 | `proposed` |
| 目标发布 | `REL-R1-RECEIVING-PILOT@0.1.0` |
| Epic | `EP-RECEIVING` |
| Feature | `FEAT-REC-REGISTRATION` |
| 开发就绪度 | `blocked` |
| 变更级别 | `minor` |
| 负责人角色 | 收样产品负责人, 收样模块工程负责人, QA负责人 |
| 影响模块 | receiving, authorization, audit, receiving-ui, automated-test |
| 来源 | PRD-MAIN#OPS-RECEIPT-001, PRD-MAIN#OD-002, PRD-MAIN#ORG-STRUCT-001, PRD-MAIN#SEC-AUTH-001, PRD-MAIN#SEC-AUD-001 |
| 固定依赖 | ED-001@0.1.0, OD-002@1.0.0, ORG-STRUCT-001@0.1.0, ORG-COLLAB-001@0.1.0, SEC-DEPLOY-001@0.1.0, OD-009@0.1.0, OPS-RECEIPT-001@0.1.0, SEC-AUTH-001@0.1.0, AC-SEC-001@0.1.0, SEC-AUD-001@0.1.0, NFR-ARCH-001@0.1.0 |
| 规格指纹 | `071aeb85adf1e36257baab4700efc2c7d38bf001a8c99597e2d29153cf412d57` |

## 业务结果

收样员可以一次登记一笔到货，明确其包装单元和实际收到的实物；后续身份、谱系和检测不再依赖含义混乱的单一 Sample 记录。

## 主要参与者

具有当前实验室收样权限的收样员

## 触发条件

收样员收到物流包裹，录入物流、包装和实物信息并提交登记

## 前置条件

- ED-001 技术栈与工程骨架已批准并落地
- OD-009 已批准试点产品的收到实物识别粒度
- 部署已绑定唯一 OrganizationGroup，客户、服务委托、归属法人、收样实验室和当前用户均在该集团上下文内
- 服务委托处于允许收样的状态且未关闭

## 正常路径

- 服务端从部署与受信身份解析集团上下文，并校验法人、实验室、客户、服务委托和收样员授权
- 创建 Receipt 到货事实并分配不可复用业务编号
- 按请求创建一个或多个 Container 包装单元
- 按 OD-009 规则创建 ReceivedItem，不把包装数量直接当作实物数量
- 新 ReceivedItem 初始状态为 REGISTERED，随后由同一事务规则进入 QUARANTINED
- 返回对象 ID、业务编号、版本和后续可执行动作
- 写入追加式审计事件和事务发件箱事件

## 失败路径

- 委托不存在，或调用方无权访问对应法人、实验室、客户或对象时按安全策略拒绝，不泄露对象是否存在
- 委托状态不允许收样时返回稳定领域错误，不创建任何到货对象
- 请求幂等键重复且载荷相同则返回首次结果，不重复创建
- 请求幂等键重复但载荷不同则返回幂等冲突
- 包装或实物数据不符合 OD-009 规则时整笔失败，不保留半成品记录
- 审计或事务发件箱写入失败时业务事务整体回滚

## 领域不变量

- Receipt、Container、ReceivedItem 是独立聚合或实体，身份和业务编号不可互换
- 业务编号在集团和对象类型命名空间内唯一且永不复用；数据库主键使用 UUID/GUID
- 新收到实物未完成身份评估前必须隔离
- 所有写入均包含 CreatedAt/By、UpdatedAt/By 和期望版本
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
    "containerIds",
    "receivedItemIds",
    "aggregateVersion"
  ],
  "receivedItem": [
    "declaredDescription",
    "declaredQuantity",
    "unit",
    "identifierGranularityEvidence"
  ],
  "required": [
    "legalEntityId",
    "laboratoryId",
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
- 授权范围同时匹配部署集团、客户、法人、实验室、对象和有效期
- 系统管理员默认没有业务收样权限
- 客户端不能提交或覆盖 organizationGroupId；跨法人、跨实验室、跨客户 ID 不能绕过对象过滤
- 合法跨实验室收样必须具有显式授权并记录归属、收样和后续执行责任

## 审计要求

- 记录 RECEIPT_REGISTERED 和 RECEIVED_ITEM_QUARANTINED
- 记录操作者、部署集团、法人、实验室、客户、服务委托、对象、请求关联ID和幂等键哈希
- 不得把完整客户敏感附件或令牌写入日志
- 失败阻断记录 ACTION_BLOCKED，但不得保存未批准敏感正文

## UX 状态

- 空态：未添加包装单元时禁止提交并提示最低资料
- 编辑态：包装单元和实物分层显示，不能把两者合并为同一行
- 提交中：防止重复点击并携带稳定幂等键
- 成功态：展示到货号、包装号、实物号和隔离状态
- 错误态：按字段错误和业务阻断分别展示，保留用户未提交内容
- 只读态：无收样权限用户可按授权查看但不能提交

## 可观测性

- 指标 receipt_registration_total 按结果和实验室聚合，不使用客户名作为高基数标签
- 指标 receipt_registration_duration_seconds
- 结构化日志包含 correlationId、operationId、organizationGroupId、legalEntityId、laboratoryId 和错误码
- 事务发件箱积压有独立告警

## 测试场景

| ID | 类型 | Given | When | Then |
|---|---|---|---|---|
| TC-REC-001-01 | positive | 合法委托和授权收样员；一个包装含两个实际实物 | 提交带幂等键的登记命令 | 创建一笔Receipt、一个Container、两个ReceivedItem；实物均进入QUARANTINED；写入审计与发件箱 |
| TC-REC-001-02 | idempotency | 首次请求已经成功 | 使用相同幂等键和相同载荷重试 | 返回首次结果；对象数量不增加 |
| TC-REC-001-03 | negative | 幂等键已绑定另一载荷 | 复用该键提交不同包装数据 | 返回IDEMPOTENCY_CONFLICT；没有新增对象 |
| TC-REC-001-04 | security | 用户只获授权法人甲、实验室甲和客户甲；服务委托属于未授权的法人乙、实验室乙或客户乙 | 提交收样 | 服务端拒绝；不泄露对象是否存在或业务信息；记录安全审计 |
| TC-REC-001-05 | recovery | 审计或发件箱持久化模拟失败 | 提交收样 | 业务对象全部回滚；重试后只产生一套对象 |
| TC-REC-001-06 | security | 部署已绑定集团甲；客户端载荷额外提交集团乙标识 | 提交收样 | 拒绝未知或禁止字段；不切换集团上下文；记录安全审计 |
| TC-REC-001-07 | authorization | 用户具有获批的跨实验室收样授权；委托归属法人、收样实验室和执行实验室均已明确 | 提交收样 | 登记成功；只授予该委托范围内权限；审计保留各责任主体和授权依据 |

## 明确非目标

- 不实现身份评估结论
- 不实现条件接收审批
- 不实现拆解、制样或检测任务
- 不实现完整客户门户
- 不决定 OD-009 的业务答案

## 允许修改路径

- `src/modules/receiving/**`
- `src/modules/audit/public-contracts/**`
- `contracts/receiving/**`
- `apps/web/receiving/**`
- `tests/receiving/**`
- `docs/domain/receiving/**`

## 验证命令

- `python -m tools.specgen check`
- `TECH_STACK_TEST_COMMAND_REQUIRED_BY_ED-001`
- `MODULE_BOUNDARY_CHECK_REQUIRED_BY_ED-001`

## 完成定义

- 数据迁移、API契约、页面、权限、审计和发件箱在同一变更集中完成
- 全部测试场景自动化并通过
- 接口和错误码文档已生成且无未声明字段
- 不存在跨模块私表访问或循环依赖
- 演示可从空数据库完成登记并查看隔离对象和审计证据
- 需求—设计—测试—证据追踪更新

## AI 执行约束

- 不得修改本文件；它由结构化规格生成。
- 不得把待决策项自行解释为默认业务规则。
- 不得访问其他模块私有表；必须使用批准的端口或事件契约。
- 若前置决策、依赖或测试夹具缺失，应停止实现并报告阻塞，不得猜测。

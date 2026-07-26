<!-- GENERATED FILE — DO NOT EDIT.
Generator: openlims-specgen@0.1.0
Sources: ATC-QTY-001@1.0.0
Edit files under spec/ and run `python -m tools.specgen generate`.
-->

# ATC-QTY-001：实施 DEV-009 不可变数量流水与并发预留

## 元数据

| 字段 | 值 |
|---|---|
| 规格版本 | `1.0.0` |
| 评审状态 | `approved` |
| 目标发布 | `REL-R1-RECEIVING-PILOT@1.0.0` |
| Epic | `EP-RECEIVING` |
| Feature | `FEAT-SAMPLE-QUANTITY` |
| 开发就绪度 | `ready` |
| 变更级别 | `major` |
| 负责人角色 | 实验室技术负责人, 技术负责人, 质量负责人, QA负责人 |
| 影响模块 | quantity, authorization, audit, outbox, availability-gate, automated-test |
| 来源 | PRD-MAIN#OD-010, PRD-MAIN#OPS-QTY-001, PRD-MAIN#OPS-QTY-002, PRD-MAIN#OPS-QTY-003, PRD-MAIN#OPS-QTY-004, PRD-MAIN#AC-QTY-001 |
| 固定依赖 | ATC-PLT-003@1.0.0, ED-001@2.0.0, OD-002@1.0.0, OD-009@1.0.0, OD-010@1.0.0, BUS-QTY-001@1.0.0, BUS-QTY-002@1.0.0, BUS-QTY-003@1.0.0, AC-QTY-001@1.0.0, SEC-AUTH-001@1.0.0, SEC-AUD-001@2.0.0, NFR-ARCH-001@2.0.0 |
| 规格指纹 | `29b0d4a4add02323e0db9200b320c25aa97dcd3661bfd49296afafadd97856a9` |

## 业务结果

实验室可以为每个收样对象建立可审计的不可变数量账，任何下游在预留或消耗样品量前可用公共端口验证精确账户版本的可用量；并发超分配、负余额和伪精确数量被系统性阻断。

## 主要参与者

具有 quantity.post 及法人、实验室、客户、委托和产品类别对象范围的授权操作人

## 触发条件

授权操作人创建数量账户或向账户追加过账条目

## 前置条件

- DEV-002 模块接入通道已交付
- 部署绑定唯一 OrganizationGroup
- 调用身份由服务端建立
- 对象引用由调用方提交精确稳定 ID 和版本（锚定 OD-009 粒度）

## 正常路径

- 校验 actor capability 和对象范围
- 建账时固定对象引用、维度、单位、精度和守恒公差并拒绝不可计量对象
- 过账时校验 expectedCurrentVersion 与锁内当前版本
- 校验条目类型、金额精度、余额与活跃预留
- 原子保存不可变流水条目、账户版本、审计和 Outbox
- 公共 QuantityAvailabilityPort 对当前账户版本返回 ALLOWED 和可用量

## 失败路径

- 缺失必需配置或引用时返回 QTY_VALIDATION_FAILED
- 维度未知、单位不匹配或金额超过精度时返回 QTY_DIMENSION_MISMATCH
- 对象不可合理计量时返回 QTY_NOT_QUANTIFIABLE
- 负余额或超过可用量时返回 QTY_INSUFFICIENT_BALANCE
- 无能力或跨范围请求返回 QTY_NOT_AUTHORIZED
- 旧 expectedCurrentVersion 返回 EXPECTED_VERSION_CONFLICT
- 冲销或重记引用不存在或已冲销条目时返回 QTY_VALIDATION_FAILED
- 规则版本或账户版本不匹配的可用量查询返回 UNKNOWN 并阻断
- 持久化、审计或 Outbox 失败时整体回滚

## 领域不变量

- 已过账条目不可修改或删除
- 一个账户只存在一个当前最高版本
- 一个账户只绑定一个对象引用、一个维度和一个单位
- 余额不得为负且有效分配不超过可用量
- 更正必须冲销原条目并追加引用原条目的重记条目
- UNKNOWN 等同阻断
- 数量流水不承载物理谱系、任务分配或保管责任链
- 不读取其他模块私表且不创建生产任务

## 数据契约

```json
{
  "account": [
    "quantityAccountId",
    "subjectType/ref/version",
    "dimension",
    "unit",
    "precisionScale",
    "conservationTolerance",
    "version",
    "ruleSetVersion",
    "createdBy",
    "createdAt"
  ],
  "dimensions": [
    "COUNT",
    "MASS",
    "LENGTH",
    "AREA",
    "VOLUME"
  ],
  "entry": [
    "entryId",
    "quantityAccountId",
    "entryType",
    "amount",
    "resultingBalance",
    "resultingReserved",
    "referencedEntryId",
    "reservationId",
    "reason",
    "actor",
    "correlationId",
    "postedAt"
  ],
  "entryTypes": [
    "RECEIPT",
    "OUTPUT",
    "RESERVE",
    "RESERVE_RELEASE",
    "ALLOCATE",
    "CONSUME",
    "RETURN",
    "LOSS",
    "DISPOSE",
    "REVERSAL",
    "RESTATE"
  ]
}
```

## API / 命令契约

```json
{
  "errors": [
    "QTY_VALIDATION_FAILED",
    "QTY_DIMENSION_MISMATCH",
    "QTY_NOT_QUANTIFIABLE",
    "QTY_INSUFFICIENT_BALANCE",
    "QTY_NOT_AUTHORIZED",
    "EXPECTED_VERSION_CONFLICT",
    "QTY_APPLICABILITY_UNKNOWN",
    "PERSISTENCE_UNAVAILABLE"
  ],
  "operations": [
    "POST /api/v1/quantity-accounts",
    "POST /api/v1/quantity-accounts/{id}/entries",
    "GET /api/v1/quantity-accounts/{id}",
    "GET /api/v1/quantity-accounts/{id}/availability"
  ],
  "publicPort": "QuantityAvailabilityPort@v1",
  "success": [
    "201 QuantityAccountResult",
    "201 QuantityEntryResult",
    "200 QuantityAccountResult",
    "200 QuantityAvailabilityResult"
  ]
}
```

## 状态转换

- NONE -> ACCOUNT@v1 by 建账
- ACCOUNT@vN -> ACCOUNT@vN+1 by append-only 过账
- 任何失败不追加条目也不推进版本

## 权限与职责分离

- 建账、过账、冲销和重记只要求 quantity.post 单一能力和既有对象范围
- 读取和可用量查询在本切片同样使用 quantity.post
- 不新增草稿编辑、发起/复核双人链或多级签署
- 客户端不能提交 OrganizationGroup
- 服务端对账户对象范围统一校验

## 审计要求

- 记录命令类型、accountId/version、entryType、金额摘要、actor、correlationId 和结果
- 失败、越权、版本冲突与事务回滚通过追加路径记录
- Outbox eventId 与账户版本一一对应
- 敏感正文不写日志或指标

## UX 状态

- 本卡不新增前端页面
- HTTP 响应返回服务端计算的余额、活跃预留、可用量和账户版本
- 客户端不得自行推算余额、伪造精度或把 UNKNOWN 当作允许

## 可观测性

- quantity_entry_posted_total 按 entryType 聚合
- quantity_gate_total 按 ALLOWED/BLOCKED/UNKNOWN 聚合
- quantity_rejected_total 按稳定原因聚合
- UNKNOWN、事务回滚和 Outbox 积压写结构化告警

## 测试场景

| ID | 类型 | Given | When | Then |
|---|---|---|---|---|
| TC-QTY-001-01 | positive | 对象引用完整且可计量；授权有效 | 建账并收货 100 克 | 创建 ACCOUNT@v1 并追加 RECEIPT；余额与可用量为 100.00；可用量查询 ALLOWED |
| TC-QTY-001-02 | boundary | 账户精度 2 位小数 | 依次过账收货、产出、预留、预留释放、分配、消耗、归还、损耗、处置 | 每笔重算余额、活跃预留和可用量；超过精度的金额被拒绝；COUNT 账户拒绝小数 |
| TC-QTY-001-03 | negative | 可用量小于请求量或维度/单位不匹配 | 提交消耗或过账 | 稳定错误；不追加条目也不推进版本 |
| TC-QTY-001-04 | negative | 对象声明不可合理计量 | 尝试建账 | QTY_NOT_QUANTIFIABLE；不创建账户或伪精确数量 |
| TC-QTY-001-05 | permission | 缺少 capability 或对象范围 | 建账、过账或查询 | 统一拒绝；追加脱敏失败审计 |
| TC-QTY-001-06 | concurrency | 可用量 100 克；两个调用使用相同 expectedCurrentVersion | 并发分配各 80 克 | 最多一笔成功；另一笔版本冲突或可用量不足；有效分配不超过 100 克 |
| TC-QTY-001-07 | recovery | 审计或 Outbox 失败 | 过账并重试 | 首笔全部回滚；重试只追加一个逻辑条目 |
| TC-QTY-001-08 | regression | 存在已过账错误条目 | 尝试改写历史并提交冲销加重记 | 数据库拒绝 UPDATE/DELETE；冲销与重记均引用原条目；旧账户版本可用量查询 UNKNOWN |

## 明确非目标

- 不实现 TestObjectAllocation 任务分配
- 不实现物理谱系或保管责任链
- 不实现复合样投入比例
- 不实现制样守恒完成门禁（AC-QTY-002 全量语义）
- 不实现跨维度或跨单位换算
- 不建设计量主数据模块
- 不新增前端工作台
- 不修改 Release baseline
- 不创建 Seal、tag、GitHub Release 或部署
- 不实现共享 SaaS 多租户

## 允许修改路径

- `spec/decisions/OD-010__v1.0.0.json`
- `spec/requirements/BUS-QTY-001__v1.0.0.json`
- `spec/requirements/BUS-QTY-002__v1.0.0.json`
- `spec/requirements/BUS-QTY-003__v1.0.0.json`
- `spec/acceptance/AC-QTY-001__v1.0.0.json`
- `spec/stories/ATC-QTY-001__v1.0.0.json`
- `generated/spec/**`
- `.planning/2026-07-26-dev-009-quantity-ledger/**`
- `OpenLIMS.slnx`
- `contracts/quantity/**`
- `src/modules/quantity/**`
- `src/host/api/**`
- `src/host/worker/**`
- `tests/architecture/**`
- `tests/unit/quantity/**`
- `tests/contract/quantity/**`
- `tests/integration/quantity/**`
- `tests/e2e/quantity/**`
- `tests/contract/labeling/OpenLIMS.Labeling.ContractTests/packages.lock.json`
- `tests/contract/platform/OpenLIMS.Platform.ContractTests/packages.lock.json`
- `tests/contract/receiving/OpenLIMS.Receiving.ContractTests/packages.lock.json`
- `tests/contract/scope/OpenLIMS.Scope.ContractTests/packages.lock.json`
- `tests/integration/platform/OpenLIMS.Platform.IntegrationTests/packages.lock.json`
- `tests/test_repository_contract.py`
- `docs/domain/quantity/**`
- `scripts/verify.ps1`
- `scripts/verify.sh`

## 验证命令

- `python -m tools.specgen ready --story ATC-QTY-001@1.0.0`
- `pwsh -File scripts/verify.ps1 -Profile task -Module quantity`
- `pwsh -File scripts/verify.ps1 -Profile architecture`
- `pwsh -File scripts/verify.ps1 -Profile contracts`
- `python -m tools.specgen check`

## 完成定义

- 追加迁移不改写 DEV-003 至 DEV-008 之前的历史
- 账户与流水条目完整、不可变且版本固定
- 全部条目类型、精度和维度边界通过测试
- 负余额、超分配、不可计量对象和 UNKNOWN 始终失败关闭且无副作用
- 权限、并发、事务、恢复、审计和 Outbox 测试通过
- 公共可用量端口只依据精确账户版本
- 无跨模块私表访问
- 全仓验证通过且二次 generate written=0
- 所有变更位于 allowed_paths

## AI 执行约束

- 不得修改本文件；它由结构化规格生成。
- 不得把待决策项自行解释为默认业务规则。
- 不得访问其他模块私有表；必须使用批准的端口或事件契约。
- 若前置决策、依赖或测试夹具缺失，应停止实现并报告阻塞，不得猜测。

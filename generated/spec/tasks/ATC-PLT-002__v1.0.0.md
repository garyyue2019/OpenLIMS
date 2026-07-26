<!-- GENERATED FILE — DO NOT EDIT.
Generator: openlims-specgen@0.1.0
Sources: ATC-PLT-002@1.0.0
Edit files under spec/ and run `python -m tools.specgen generate`.
-->

# ATC-PLT-002：实施 DEV-017 事务内审计和发件箱正式化与全链验证

## 元数据

| 字段 | 值 |
|---|---|
| 规格版本 | `1.0.0` |
| 评审状态 | `approved` |
| 目标发布 | `REL-R1-RECEIVING-PILOT@1.0.0` |
| Epic | `EP-PLATFORM` |
| Feature | `FEAT-PLT-AUDIT-OUTBOX` |
| 开发就绪度 | `ready` |
| 变更级别 | `major` |
| 负责人角色 | 技术负责人, 质量负责人, 安全负责人, QA负责人 |
| 影响模块 | platform, audit, outbox, migration, cross-module, scope, quantity, allocation, batch, result, billing, automated-test |
| 来源 | PRD-MAIN#SEC-AUD-001, PRD-MAIN#SEC-AUD-002, PRD-MAIN#NFR-ARCH-001, PRD-MAIN#AC-SEC-001 |
| 固定依赖 | ED-001@2.0.0, OD-002@1.0.0, BUS-PLT-001@1.0.0, SEC-AUD-001@2.0.0, SEC-AUTH-001@1.0.0, AC-SEC-001@1.0.0, NFR-ARCH-001@2.0.0, ATC-SCP-001@1.0.0, ATC-QTY-001@1.0.0, ATC-ALLOC-001@1.0.0, ATC-BATCH-001@1.0.0, ATC-RESULT-001@1.0.0, ATC-BILL-001@1.0.0 |
| 规格指纹 | `83df7240aff412e3206daa84239bf8c70f7598e80c810c4a3f31cabce984a433` |

## 业务结果

审计不可变从应用约定升级为数据库强制，普通管理员无法篡改或删除审计与发件箱事件；已交付六模块的跨模块协作首次获得真实端口的端到端组合证据，防止桩测试掩盖组合缺陷。

## 主要参与者

平台维护者（迁移）与链路各模块操作者（端到端验证中的固定测试身份）

## 触发条件

平台维护者应用 platform-0002 迁移；QA 在专用数据库上执行全链端到端验证

## 前置条件

- platform-0001 已存在（ATC-PLT-003）
- 六张链路卡均已交付且其状态端口可用
- receiving 资格端口不在本卡范围内（其模块早于端口纪律交付），端到端验证中以许可桩替代并显式注明

## 正常路径

- platform-0002 迁移为 platform.audit_intent 安装拒绝 UPDATE/DELETE 的触发器，为 platform.outbox 安装拒绝 DELETE、仅允许 dispatched_at 从空置值的触发器，并登记迁移历史
- 就绪探针在 platform-0001 与 platform-0002 均存在时报告就绪
- 全链验证：真实端口下 创建范围矩阵→建数量账户并入账→创建测试对象分配（范围/数量真实门禁）→创建批次并挂载试样成员（分配真实门禁）→创建结果组（批次真实门禁）并记录初测/采用规则/复测/采用→生成计费证据（采用真实门禁）
- 链路每步命令在 platform.audit_intent 留下动作记录且顺序可追，platform.outbox 含每步事件类型
- 发件箱派发标记（dispatched_at 从 null 置值）成功且不可重复
- 冒烟脚本改为断言审计/发件箱删除被拒绝，不再删除审计证据

## 失败路径

- UPDATE 或 DELETE platform.audit_intent 返回 55000（PLT.AUDIT_APPEND_ONLY）
- DELETE platform.outbox、修改除 dispatched_at 外任意列或重复派发标记返回 55000（PLT.OUTBOX_DISPATCH_ONLY）
- 链路中途门禁 BLOCKED（如以过期分配版本挂载批次成员）时本步失败关闭：无成员事实，仅模块 audit_attempt 记录失败
- platform-0002 未应用时就绪探针报告未就绪

## 领域不变量

- 审计意图与发件箱事件与业务事实同事务提交或一起回滚
- 审计不可变与发件箱仅派发更新由数据库触发器强制而非应用约定
- TRUNCATE 属测试基础设施操作，不影响行级触发器语义（既有各模块集成测试的清理不受破坏）
- 端到端验证使用专用数据库 openlims_chain_test，不与各模块专用测试库互相干扰
- 跨模块消费方对端口决策与版本原样固定（gate-then-commit），本卡不放宽任何既有门禁
- 不修改任何模块业务代码与既有规格，不触碰未决 OD

## 数据契约

```json
{
  "auditIntent": [
    "append-only：INSERT 允许，UPDATE/DELETE 触发 PLT.AUDIT_APPEND_ONLY(55000)"
  ],
  "migration": [
    "platform-0002：audit_intent 追加式触发器 + outbox 派发限定触发器 + migration_history 登记"
  ],
  "outbox": [
    "INSERT 允许",
    "UPDATE 仅 dispatched_at null→非空且其余列不变，否则 PLT.OUTBOX_DISPATCH_ONLY(55000)",
    "DELETE 触发 PLT.OUTBOX_DISPATCH_ONLY(55000)"
  ]
}
```

## API / 命令契约

```json
{
  "errors": [
    "PLT.AUDIT_APPEND_ONLY",
    "PLT.OUTBOX_DISPATCH_ONLY"
  ],
  "operations": [],
  "publicPort": "无新增端口——本卡交付平台迁移与端到端验证，不新增 HTTP 端点或跨模块契约"
}
```

## 状态转换

- outbox 行：未派发（dispatched_at null）→ 已派发（dispatched_at 非空），单向且仅一次

## 权限与职责分离

- 不新增能力或 claim；端到端验证以各模块既有授权端口的许可桩替代 HTTP claims（模块自身能力语义不变）

## 审计要求

- 平台审计不可变升级为数据库强制；链路每步审计意图完整性由端到端测试固定

## UX 状态

- 本卡不新增前端页面
- 无客户端交互面——交付物为平台迁移与测试

## 可观测性

- 无新增指标；全链组合回归由 FullyQualifiedName~Platform 过滤器纳入 CI

## 测试场景

| ID | 类型 | Given | When | Then |
|---|---|---|---|---|
| TC-PLT-002-01 | positive | 专用库已应用平台与六模块迁移；单一 DI 容器装配六模块真实服务与端口 | 按 范围→数量→分配→批次→结果→计费 顺序执行命令 | 每步事实存在；platform.audit_intent 按顺序含每步动作；platform.outbox 含每步事件类型；计费证据固定采用目标与组版本 |
| TC-PLT-002-02 | negative | 链路执行至批次 | 以过期分配版本挂载试样成员 | 批次成员不产生；batch.audit_attempt 记录失败；无新增平台审计意图或发件箱事件泄漏 |
| TC-PLT-002-03 | negative | 已有审计意图行 | UPDATE 或 DELETE platform.audit_intent | PostgresException 55000；行内容不变 |
| TC-PLT-002-04 | boundary | 已有未派发事件行 | DELETE、改 message_type、置 dispatched_at、再次置 dispatched_at | DELETE 与改列拒绝 55000；首次派发标记成功；重复派发标记拒绝 55000 |
| TC-PLT-002-05 | regression | platform-0001 已应用 | 应用 platform-0002 两次并查询就绪探针 | 无副作用；迁移历史各登记一次；就绪探针为真；缺 platform-0002 时为假 |
| TC-PLT-002-06 | regression | 冒烟流程产生的审计与发件箱证据 | 冒烟收尾 | 删除审计/发件箱被 55000 拒绝并被断言；合法派发标记成功；不再删除审计证据 |

## 明确非目标

- 不实现发件箱派发器或消费者（Worker 仍为空转）
- 不改动各模块业务代码、schema 或端口契约
- 不把 receiving 资格端口纳入真实链路（其模块的端口纪律改造为后续卡）
- 不新增能力、claim 或端点
- 不触碰未决 OD，不创建 Seal、tag、GitHub Release 或部署

## 允许修改路径

- `spec/requirements/BUS-PLT-001__v1.0.0.json`
- `spec/stories/ATC-PLT-002__v1.0.0.json`
- `generated/spec/**`
- `.planning/2026-07-26-dev-017-platform-audit-outbox/**`
- `OpenLIMS.slnx`
- `src/building-blocks/**`
- `tests/e2e/chain/**`
- `tests/e2e/smoke/**`
- `tests/integration/platform/**`
- `tests/architecture/**`
- `tests/test_repository_contract.py`
- `docs/domain/platform/**`

## 验证命令

- `python -m tools.specgen ready --story ATC-PLT-002@1.0.0`
- `pwsh -File scripts/verify.ps1 -Profile task -Module platform`
- `pwsh -File scripts/verify.ps1 -Profile architecture`
- `python -m tools.specgen check`

## 完成定义

- platform-0002 数据库强制生效且幂等
- 全链真实端口端到端验证与失败关闭用例通过
- 冒烟脚本不再删除审计证据并断言不可变
- 既有 30 个测试项目全部保持绿色
- 全仓验证通过且二次 generate written=0
- 所有变更位于 allowed_paths

## AI 执行约束

- 不得修改本文件；它由结构化规格生成。
- 不得把待决策项自行解释为默认业务规则。
- 不得访问其他模块私有表；必须使用批准的端口或事件契约。
- 若前置决策、依赖或测试夹具缺失，应停止实现并报告阻塞，不得猜测。

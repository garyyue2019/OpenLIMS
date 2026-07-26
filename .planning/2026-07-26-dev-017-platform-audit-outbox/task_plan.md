# DEV-017 事务内审计和发件箱（正式化 + 全链验证）

## 目标

为已在 8 个模块落地的平台审计/Outbox 行为补上正式任务卡：新增 platform-0002 迁移以数据库强制审计不可变（SEC-AUD-002 缺口）与 Outbox 仅派发更新，并交付首个真实跨模块全链 E2E（scope→quantity→allocation→batch→result→billing 五个真实端口门禁）。

## 阶段

1. [completed] 侦察：SEC-AUD-001/002 在基线且 SEC-AUD-001@2.0.0 已批准；发现 platform.audit_intent/outbox 无 DB 级追加保护（模块 audit_attempt 均有）；各模块集成测试全部使用桩端口，跨模块组合从未真实验证。ATC-PLT-002 无未决 OD 阻断。
2. [completed] 基线依授权收敛：platform-0002 迁移（audit_intent 拒绝 UPDATE/DELETE；outbox 拒绝 DELETE、仅允许把 dispatched_at 从 null 置值且不改其他列）；IsCurrentAsync 扩展检查 platform-0002；新增 tests/e2e/chain 全链 E2E（真实 Postgres + 全模块 DI + 5 个真实端口，receiving 资格端口按卡范围外说明使用许可桩）。
3. [completed] 创建 BUS-PLT-001 + ATC-PLT-002@1.0.0（依赖六张链路卡）并 READY。
4. [completed] 实现 platform-0002 迁移与 IsCurrentAsync 扩展。
5. [completed] 全链 E2E 测试（专用 openlims_chain_test 库）+ 平台不可变测试。
6. [completed] 完整门禁，CI 全绿后按授权自动提交/PR/合并。

## 约束

- TRUNCATE 属测试基础设施操作不受行级触发器影响，既有各模块集成测试不受破坏。
- Outbox 派发语义（dispatched_at 单向置值）为既有列的最小 DB 强制，不实现派发器本身。
- 不触碰任何未决 OD；PRD 只读。

## 错误记录

| 错误 | 尝试 | 处理 |
|---|---:|---|
| `ATC-PLT-002@1.0.0` 不存在 | 1 | 预期缺口；起草任务卡。 |

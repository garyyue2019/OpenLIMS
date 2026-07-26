# DEV-010 发现

## 前置门禁

- 当前基线为 `main@4f23cad`，DEV-009（PR #9，`9b8328e`）已合并。
- `validate` 通过：99 个规格版本；SOURCE CURRENT；impact 无影响项。
- `ATC-ALLOC-001@1.0.0` 尚不存在；就绪报告与 catalog 均无 ALLOC 规格。

## 来源语义（PRD）

- OPS-ALLOC-001（L799）：任务分配时必须校验身份、状态、可用量、方法适用性、保存条件和有效期。
- OPS-ALLOC-002（L800）：必须记录预留量、领用量、预计消耗量、实际消耗量和归还量。
- OPS-ALLOC-003（L801）：每次任务使用必须通过 TestObjectAllocation 引用物理对象、身份映射版本、范围行、计划/序列步骤、用途、顺序、破坏性和预留状态。
- OPS-ALLOC-004（L802）：SampleIdentityAssignment、TestObjectAllocation、CoverageDecision 独立；修改分配不得改写实物身份。
- 10.7-4（L491）：分配同时引用送检项、适用变体/特征、范围行、物理对象和身份映射版本。
- 10.7-5（L492）：非破坏性任务用 UsageEvent 表达（本卡非目标）。
- 10.7-8（L495）：超分配、负余额、不兼容单位、互斥破坏任务或无批准覆盖被阻止。
- RULE-004（L1073）：任务使用必须由 TestObjectAllocation 表达；身份归属多义时阻断。`spec/rules/RULE-004__v0.1.0.json` 激活条件为"身份粒度、任务分配和覆盖对象契约批准后启用"。
- 11.6 任务状态机（待分配→已分配→…）属任务侧，不在分配切片内。
- **AC-ALLOC-001（L1247）是 BusinessOps 收款核销，不可用作任务分配验收锚点**。可用锚点：AC-ELEC-003（L1196，破坏后样机不得继续分配至要求原始结构的任务）、AC-REC-001（身份评估未完成禁止分配）、AC-QTY-001（并发超分配，已被 DEV-009 使用）。

## 决定基础

- PRD 未决决定表中**没有**任务分配专属 OD；L1482 要求未批准决定不得默认进入生产。
- 可依赖的已批准决定：`OD-002@1.0.0`（单集团）、`OD-009@1.0.0`（实物粒度）、`OD-010@1.0.0`（计量口径）、`OD-027@1.0.0`（范围行粒度）、`OD-035@1.0.0`（资格端口动作含 TEST_OBJECT_ALLOCATION，且明确将分配业务实现列为 deferred_scope——本卡正是补齐该延迟范围）。
- OD-004（哪些产品/方法允许破坏性拆解）仍 open：本切片把破坏性作为调用方声明的分配属性，不决定产品/方法级破坏性主数据，不触碰 OD-004。

## 工程约束（关键）

- `PostgresTransactionContext.Push` 对嵌套事务**直接抛出** `PLT.NESTED_TRANSACTION_NOT_SUPPORTED`（AsyncLocal 单例，跨模块不可绕过）。
- Quantity/Scope 公共端口内部各自调用 `transactionCoordinator.ExecuteAsync`——**分配服务必须在自身事务之外先调用端口（gate），再开自身事务提交事实（commit）**；门禁结果以版本固定快照写入分配事实，跨模块不做单事务原子性。
- DI 组合：所有模块注册进同一 IServiceCollection，端口均 Scoped，AllocationModule 可直接构造注入 `IQuantityAvailabilityPort`、`IScopeProductionEligibilityPort`、`IReceivingEligibilityPort`。
- 架构测试允许模块引用其他模块 `contracts/*`（公共契约），禁止引用 `src/modules/*` 私有实现；allocation→{receiving,quantity,scope} 无环。
- Epic/Feature 稳定标识：`EP-EXECUTION` / `FEAT-TASK-ALLOCATION`（backlog L95/L101；建议任务卡 L172 `ATC-ALLOC-001 任务分配资格`）。

## 规格输入

- OPS-ALLOC-001~004、AC-ELEC-003、AC-ID-001、AC-LIN-001、AC-STATE-001 均在已确认来源基线（reviewed 2026-07-23）。
- 后继规格最小集合：三项 BUS-ALLOC requirement、一项 AC-ELEC-003@1.0.0 验收、一张 `ATC-ALLOC-001@1.0.0` 任务卡；无需新增 OD（无未决分配决定）。
- 平台依赖沿用：`ATC-PLT-003@1.0.0`、`ED-001@2.0.0`、`SEC-AUTH-001@1.0.0`、`SEC-AUD-001@2.0.0`、`NFR-ARCH-001@2.0.0`。

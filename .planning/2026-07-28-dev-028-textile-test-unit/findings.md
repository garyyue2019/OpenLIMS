# Findings: DEV-028 纺织运行时

## Gate evidence

- 起始工作树为干净的 `main...origin/main`。
- `specgen validate`：189 个规格版本、389 个 PRD 来源条目，通过。
- `specgen source-status`：SOURCE CURRENT。
- `specgen impact`：无当前变更和来源漂移。
- 新 Story 创建前的 `ready` 预检按预期失败：`ATC-TEX-004@1.0.0` 尚不存在。
- 治理完成后 strict validate 通过：195 个规格版本 / 389 个来源条目；`ATC-TEX-004@1.0.0` READY，history 通过。
- 第一次 generate 写入 12 个派生文件，check 通过；追加不可覆盖快照 `spec/baselines/dev-028-textile-runtime-baseline.lock.json`。

## Governance findings

- `OD-035@1.0.0` 已批准并绑定 DEV-005 隔离与身份评估，绝不能用于纺织。
- `OD-001@1.0.0` 批准玩具 + 物理机械为 R1 唯一试点，并明确纺织运行时为 DISABLED；启用是激活语义变化，必须创建 MAJOR 后继版本。
- `ATC-TEX-001@1.0.0` 和 `ATC-TEX-003@1.0.0` 只批准纯契约切片，不含模块、schema、HTTP、权限或运行时端口。
- `ATC-TEX-003@1.0.0` 明确记录 ATC-TEX-002 已跳过；新运行时任务应使用新的稳定 Story ID，避免重写已批准历史叙述。
- 用户已明确批准启用纺织运行时，但没有提供真实付费灯塔或生产部署证据；实现可进入代码和测试，生产上线仍受既有证据门禁约束。
- `OD-036@1.0.0` 拥有运行时实现/受控验证边界；`OD-001@1.0.0` 原样保留并继续拥有玩具唯一生产试点。
- BUS-TEX-001/002/003 与 AC-TEXTILE-001 的 v1 业务内容保持不变，仅生命周期转为 deprecated；v2.0.0 为唯一当前 approved 版本。

## Implementation boundary

- 复用 `contracts/textile` 的 DEV-011/012 契约与纯规则，不从运行对象推导“最新版”。
- 参考已交付 Toy TestUnit 的服务、权限、事务、审计、Outbox、迁移和测试模式，但不复制玩具业务语义。
- 新迁移必须单调追加；不得修改任何已发布迁移。
- DEV-028 仅生产化 DEV-011 的样品需求与 CuttingPlan；DEV-012 的调湿/洗涤及超差仍保持纯契约，后续独立任务生产化。
- OD-001 的 schema 不支持 decided 决策退役且 strict 禁止同 ID 多个 approved；因为本任务不改变唯一生产试点，运行时实现/受控验证边界由新决策 `OD-036@1.0.0` 拥有。Story 使用 `ATC-TEX-004@1.0.0`，避免与已跳过的 TEX-002 历史叙述冲突。
- 运行时沿用批准契约中的规则集固定、互斥共享拒绝、面积不足缺口和 UNKNOWN 失败关闭，不引入行业默认尺寸、规则或审批替代值。

## Runtime architecture baseline

- `contracts/textile` 当前只有纯契约与确定性规则；不存在 `src/modules/textile`、Textile HTTP、持久化或运行时测试项目。
- 新 `OpenLIMS.Modules.Textile` 项目将只引用 Textile/Platform contracts 与 Platform building blocks；DEV-028 不需要 Quantity/Allocation 项目引用。
- 模块实现 `IOpenLimsApiModule`、`IOpenLimsWorkerModule`、`IOpenLimsMigrationModule`，提供稳定 descriptor `textile`，按单调顺序应用首个 Textile migration。
- 复用平台 `ITransactionCoordinator`、`IAuditIntentWriter`、`IOutboxWriter` 与请求上下文/授权模式；不复制平台基础设施，也不访问其他 schema。
- 先新增 Textile unit/contract/integration 测试项目并纳入解决方案，再用缺失类型/服务形成可解释 RED。
- 运行服务从 `ICurrentOrganizationContext` 与 `ICurrentActorContext` 取得可信范围/行为人，使用模块自有授权端口校验 capability + legal entity + laboratory，不接受客户端提交这些可信字段。
- 写路径由平台 `ITransactionCoordinator` 包裹；Store 在同一连接/事务内写业务表、`IAuditIntentWriter` 和 `IOutboxWriter`，失败路径由模块自有 attempt writer 追加。
- 创建计划采用 aggregate advisory lock + `expectedCurrentVersion`；批准固定计划/需求的 input hash 与 rule set，状态端口对未知证据返回 UNKNOWN。
- 测试项目沿用仓库中央包版本和 SDK 默认 Compile，项目文件只需 Framework/Package/ProjectReference；避免把 bin/obj 枚举结果当作源码清单。
- 当前 main 的 `ToyConclusionPersistence.cs` 引用全仓不存在的 `ITransactionToken`/`NpgsqlTransactionToken`，API 宿主构建会在进入 Textile 测试前失败；该文件不在 DEV-028 allowed_paths。
- Textile HTTP 契约采用模块级 `TestServer`，直接验证路由、状态码、problem 和 endpoint name 元数据，避免用越界修复掩盖主干缺陷；静态宿主 OpenAPI 文本另由允许范围内的 host/仓库测试覆盖。
- 当前正确数据库协作方式是 `IPostgresTransactionAccessor`：平台 coordinator 通过 AsyncLocal 暴露同一 Npgsql connection/transaction，模块 Store 只在活动事务内访问自身 schema。
- `IAuditIntentWriter` 与 `IOutboxWriter` 要求活动平台事务；业务 insert 后在同事务写证据，任一步异常由 coordinator 回滚。
- Textile 将使用模块自有 `NpgsqlDataSource` 仅做独立失败尝试/迁移；正常业务 Store 只使用 ambient accessor，避免出现 ToyConclusion 的不存在 token 类型。
- 迁移模式：模块级 advisory migration lock、`migration_history`、`create if not exists`、所有事实表 UPDATE/DELETE 触发 SQLSTATE 55000，版本只追加。
- 服务失败策略：可信 actor/scope 前置校验；领域/Npgsql 异常统一映射稳定错误码并在主事务外写 `audit_attempt`；若失败尝试本身不可写则提升为 persistence unavailable。
- GET 读取也写 audit_intent，但不写 Outbox；创建需求/计划/批准各写一个业务 Outbox，样品不足的需求使用专用 shortage message type。
- API host 与 Worker 都通过 `IOpenLimsServerModule[]` 目录注册；Textile 必须加入两者并添加项目引用，Worker 因无 Toy 引用可独立验证迁移接线。
- Worker 含 Textile 的 Release 构建已通过；API restore 也通过，说明项目引用/包锁有效。API 编译失败仅来自未改动的 ToyConclusionPersistence 不存在类型，与 Textile 接线无新增编译错误。
- 架构测试已有逐模块 schema 正则扫描；新增 Textile 测试应只允许 `textile.*`，并允许显式平台 audit/outbox 公共端口调用而不出现其他模块私表名。
- Python repository contract 当前没有现成的模块/OpenAPI文本断言，DEV-028 将新增针对 Textile module reference、四个 operationId 和 migration/solution 路径的回归检查。
- 旧 `r1-applicability-baseline.lock.json` 在 Python 仓库测试中与当前 lock 逐哈希核对 OD-001/BUS-GOV；其业务说明同时要求 v1 Textile 保持 DISABLED。为保持冻结基线，所有 BUS-TEX/AC v1 必须原字节恢复，运行时启用不能通过同 ID 生命周期修改表达。
- 最终规格采用新稳定 ID：BUS-TEX-006（运行时事实）、BUS-TEX-007（不足/互斥批准门禁）、BUS-TEX-008（CuttingPlan 运行时）和 AC-TEXTILE-004（运行时验收）；原 v1 契约继续 approved/DISABLED 并作为精确依赖。
- 最终 generate 后 impact 归零，四个旧 v1 文件 `git diff --exit-code` 为 0，R1 snapshot 逐哈希冻结测试通过；历史边界已恢复。
- `dev-028-textile-runtime-baseline.lock.json` 保留中间评审状态，`dev-028-textile-runtime-baseline-final.lock.json` 冻结最终新-ID规格状态；两者均追加且不可覆盖。
- API/Worker 项目新增 Textile `ProjectReference` 后，引用这两个宿主的下游测试项目必须重新计算 NuGet lock；`--locked-mode` 的 NU1004 是锁文件不同步而非包解析或 Textile 代码失败，任务卡已允许 `tests/**/packages.lock.json` 的机械刷新。
- `test_platform_major_machine_drafts_are_unapproved_and_dependency_scoped` 用 `planned_refs | approved_delivery_v1_refs` 精确等于全部 v1 对象来防止未分类规格。失败列出的 10 个对象不是未知漂移，而是已存在但未入显式清单的 Toy 交付 `OD-034/BUS-TOY-006/AC-TOY-002/ATC-TOY-004`，以及 DEV-028 新增 `OD-036/BUS-TEX-006/007/008/AC-TEXTILE-004/ATC-TEX-004`；修复应扩充 approved 集合而非放宽断言。
- 10 个遗漏对象均为 approved；除 `AC-TOY-002` 外均内联“用户”批准证据。`AC-TOY-002` 的 owning Story `ATC-TOY-004` 为 approved、精确依赖该 AC，且顶层批准证据含“用户”。测试可显式验证此证据链，保持门禁强度而不越界改写 Toy 规格。
- 最终任务脚本确认 full solution 唯一编译阻断仍是 `src/modules/toy/OpenLIMS.Modules.Toy/ToyConclusionPersistence.cs` 两处 `ITransactionToken` CS0246；Textile 模块、Worker、unit/contract/integration 项目均在同次 build 成功。该 Toy 文件不在 ATC-TEX-004 allowed_paths，不能为完成 DEV-028 越界修复。

# DEV-008 发现

## 前置门禁

- 当前基线为 `main@77f5dae`，DEV-007 已合并。
- `validate` 通过：87 个规格版本、389 个 PRD 来源条目。
- `source-status` 为 SOURCE CURRENT。
- `impact` 无直接或传递影响项。
- `ATC-SCP-001@1.0.0` 尚不存在，不能直接进入实现。

## 已确认方向

- 用户明确选择先不发布，继续推进 DEV-008。
- DEV-008 采用仓库建议任务 `ATC-SCP-001：ScopeLine 生产可用门禁`。
- Release baseline、Seal 和部署均不属于本任务。

## 待核对

- ScopeLine 已有结构化需求、决定、验收规格及其状态。
- 当前代码是否已有范围、适用性、版本锁或资格端口可复用。
- 最小可交付切片的状态、权限、并发、审计和失败关闭边界。

## 来源语义

- PRD 已定义 `TestScopeMatrix` 为版本化业务基线，`ScopeLine` 是最小可审批、可变更、可执行、可追溯单元。
- 每个 ScopeLine 必须固定送检对象/特征、目标市场、要求条款、项目、方法版本、样品需求、`EvaluationMode`、工作中心和报告位置。
- `EvaluationMode=EVALUATED` 才强制限值和判定规则；`MEASURED_ONLY`、`NOT_EVALUATED`、`WAIVED` 不得伪造符合性决定。
- 未经技术批准的客户勾选、历史套餐、BOM 或 AI 候选不得生成生产任务。
- 已被报价、任务、结果或报告引用的范围行不得原地修改，语义变化必须形成新版本和影响清单。
- `SampleIdentityAssignment`、`TestObjectAllocation`、`CoverageDecision` 是独立关系，DEV-008 不应提前实现任务分配或代表性覆盖工作流。
- 当前 `src/`、`contracts/`、`apps/` 和 `tests/` 尚无 ScopeLine/TestScopeMatrix 实现，可按新垂直切片建立边界。

## 阻断决定

- `OD-027@0.1.0` 当前为 `proposed`、`decision_state=open`、`decision=null`。
- 候选粒度为：项目级、方法选项级、对象特征乘项目级。
- PRD 的必需引用和 AC-SCOPE-001 更接近“送检对象/特征 × 检测项目 × 方法选项”，但该结论仍需要用户明确批准，AI 不能据此把 OD-027 标记 approved。
- 该粒度决定会影响 ScopeLine 唯一性、版本变化、报价/任务/报告引用和迁移结构，必须在任务卡 READY 前确定。

## 用户批准

- 用户于 2026-07-26 明确回复“批准 DEV-008 业务基线”。
- 批准范围包括：对象/特征 × 目标市场/要求条款 × 检测项目粒度；单一方法版本/选项；EvaluationMode 条件语义；批准后不可原地修改；`scope.approve` 单一能力；缺失或 UNKNOWN 失败关闭。
- 非目标为报价、任务分配、代表性覆盖、Release baseline、Seal 和部署。

## 结构化规格输入

- `BUS-SCOPE-001`、`BUS-SCOPE-002`、`BUS-SCOPE-003` 和 `AC-SCOPE-001` 均已存在于已确认 PRD 来源基线，但尚无独立结构化规格文件。
- `OD-027@0.1.0` 是未决定旧草案；应追加 `OD-027@1.0.0`，不能改写旧文件。
- 后继规格最小集合为 `OD-027@1.0.0`、三项 BUS-SCOPE requirement、`AC-SCOPE-001@1.0.0` 和 `ATC-SCP-001@1.0.0`。
- 任务卡可精确依赖现有 `ATC-PLT-003@1.0.0`、`ED-001@2.0.0`、`SEC-AUTH-001@1.0.0`、`SEC-AUD-001@2.0.0` 与 `NFR-ARCH-001@2.0.0`，不需要发布基线审批。
- 仓库已给出 `EP-SCOPE-COMMERCIAL` 与 `FEAT-SCOPE-LINE-GATE`，DEV-008 直接使用这两个稳定标识。
- 为保持轻治理，授权人通过一次命令创建不可变 APPROVED 矩阵版本；修订通过后继版本完成，不增加草稿编辑、双人复核或前端工作台。

## 工程接入

- 现有业务模块通过 `IOpenLimsApiModule`、`IOpenLimsWorkerModule`、`IOpenLimsMigrationModule` 接入 Catalog；Scope 应沿用相同组合点。
- 新模块需要独立 `contracts/scope`、`src/modules/scope` 和三层 scope 测试项目，并在 `OpenLIMS.slnx`、API/Worker 项目引用和 Catalog 构造中显式注册。
- Scope 模块只访问自己的 `scope` schema；跨模块只暴露版本化公共资格端口，不读取 Receiving 或其他模块私表。
- 迁移由 Worker 的模块迁移入口执行；API 与 Worker 共用精确连接字符串和模块描述符。
- 平台已经提供同一 PostgreSQL 事务内的 `ITransactionCoordinator`、`IAuditIntentWriter`、`IOutboxWriter` 和 `IPostgresTransactionAccessor`；Scope 可复用公共端口实现原子提交，无需复制其他模块私有审计表。
- 平台 Audit/Outbox 仅保存稳定元数据，Scope 业务事实和固定引用仍保存在 `scope` schema；失败尝试需使用独立追加路径，避免事务回滚抹除拒绝证据。
- API/Worker 的模块数组是显式组合根，需要加入 `ScopeModule`；API 还要注册 Scope 项目引用和 OpenAPI 契约。
- 现有 HTTP 授权采用精确 claim 匹配；Scope 应复用 organization、capability、legal_entity、laboratory、customer、service_order、product_category 维度，不从请求体接受集团。
- Endpoint 采用 Minimal API、集中 JSON 选项、CorrelationId 和稳定 Problem Details 映射；Scope 可沿用该契约，不引入控制器框架。
- 业务服务应在事务内锁定当前版本、授权、校验、写事实和平台审计/Outbox；领域错误与 Npgsql 错误统一失败关闭，拒绝尝试通过独立追加 writer 保留。
- 追加迁移使用 advisory lock、`create table if not exists`、migration_history 和数据库触发器拒绝 UPDATE/DELETE；Scope 历史不可变性应同时由领域和数据库保证。
- 公共资格端口应像 Receiving v2 一样返回 `ALLOWED/BLOCKED/UNKNOWN`、固定规则版本和原因码；规则版本或请求版本不匹配必须返回 UNKNOWN。
- Scope 授权契约可沿用现有精确维度和 claim 名称，但保持独立公共类型，避免 Contracts.Scope 依赖 Contracts.Receiving。
- `scope.approve` 是唯一业务 capability；读取和资格查询在本切片也使用该能力，避免新增未批准的权限语义。
- Contracts 项目无外部包，锁文件为空依赖集合；Scope 可与 Receiving contracts 使用相同最小项目形态。
- Scope 单元、契约、集成测试项目可分别复用现有测试 SDK/xUnit/MvcTesting/Npgsql 精确包集合；新增项目引用不改变 Host 的包锁。
- API OpenAPI 是代码内显式路径字典，Scope 四个端点必须同步加入，不能只注册运行时路由。
- 本机存在用户级 .NET SDK `C:\Users\Administrator\.dotnet\sdk\10.0.302`；应使用该精确可执行文件恢复和构建，避免系统 PATH 上的 .NET 9 误报缺失。
- 契约测试通过 WebApplicationFactory 替换模块服务，可在无 PostgreSQL 时验证四个 HTTP 端点、Problem Details、CorrelationId 和 OpenAPI。
- 架构测试需显式加入 Scope contracts 根、Host manifest 断言和 `scope` 私有 schema 断言；现有路由扫描只检查 Program 内技术路由，不会把模块 Endpoint 误判为 Host 硬编码路由。
- `ScopeProductionEligibilityPort` 已对调用上下文不匹配执行脱敏失败审计，但数据库事务内 `IScopeAuthorizationPort` 返回拒绝时抛出领域异常且当前仅捕获 `NpgsqlException`，会漏记失败尝试；需要在事务回滚后独立追加 `scope.audit_attempt` 再重抛。
- Scope 集成测试应先运行 `PlatformMigrationRunner.ApplyAsync` 再运行 `ScopeMigrator.ApplyAsync`，因为成功审计和 Outbox 使用平台公共写入端口而非 Scope 私表。
- 当前生成基线包含 21 个任务 Markdown 和 30 个 feature；DEV-008 新增精确文件为 `ATC-SCP-001__v1.0.0.md`、`ATC-SCP-001__v1.0.0.feature` 与 `AC-SCOPE-001__v1.0.0.feature`。
- API 新增 Scope 项目引用会使所有引用 API Host 的测试项目锁图新增 Scope 传递项目节点；锁文件必须同步，否则 solution locked restore 不再代表真实依赖图。
- Scope 需要沿用现有 `scripts/verify.ps1` / `scripts/verify.sh` 任务入口；任务卡已精确补入两份脚本和四份受影响锁文件，脚本只增加 `Profile=scope` 映射。
- 本机无 PostgreSQL 服务/客户端、无 Docker、无 Bash；不能在不安装外部基础设施的情况下真实执行 Scope PostgreSQL 集成用例。

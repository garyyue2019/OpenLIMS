# Task Plan: DEV-028 纺织运行时启用与 TestUnit 实施

## Goal

在不复用稳定 ID、不改写已封存规格、不改变玩具作为 Release 1 唯一生产试点的前提下，把既有纺织契约切片生产化为可运行模块：样品需求、CuttingPlan、技术批准、持久化、HTTP、权限、审计、遥测和完整测试。

## User approval

- 2026-07-28 用户在获知 OD-001 当前禁用纺织运行时、且 DEV-028 需要先做治理后，明确回复：`启用纺织运行`。
- 该批准授权纺织模块的运行时实现与注册；它不自动构成真实付费灯塔、生产部署或替换玩具唯一 R1 试点的证据。
- 2026-07-28 用户在获知 DEV-027 Toy 结论运行时编译、签署/SoD 与测试缺口，以及现有任务卡路径失配后回复：`按你建议的做`。该授权允许先隔离 DEV-028，再为同一 DEV-027 创建 SemVer 后继修复卡并完成受控修复；不授权虚构签署、SoD 或生产证据。

## Current Phase

Phase 5: cross-task unblock handoff

## Phases

### Phase 1: governance and READY task card

- [x] 恢复计划上下文并确认工作树干净
- [x] 运行 validate、source-status、impact
- [x] 确认旧计划错误：OD-035 已分配给 DEV-005；ATC-TEX-002 已在批准规格中记录为跳过
- [x] 创建 OD-036 决策，启用纺织运行时实现/受控验证但保留 OD-001 玩具唯一生产试点边界
- [x] 创建新的稳定 Story（不复用 ATC-TEX-002），声明完整 allowed_paths、测试与非目标
- [x] 通过 strict validate、source-status、impact、ready、generate/check，并追加不可覆盖 snapshot
- **Status:** complete

### Phase 2: test-first contract and domain extension

- [x] 先添加领域/HTTP 正向、反向和边界测试；权限、并发、恢复和审计由 Phase 3 PostgreSQL RED 覆盖
- [x] 扩展 Textile 公共契约，保持既有序列化兼容
- [x] 实现样品需求、CuttingPlan 与技术批准领域规则
- **Status:** complete

### Phase 3: runtime module

- [x] 实现应用服务、追加式持久化和新迁移
- [x] 实现 HTTP 端点、能力校验、审计/Outbox 和遥测
- [x] 注册 Textile 模块及公共状态端口，不访问其他模块私表
- **Status:** complete

### Phase 4: verification and handoff

- [ ] 使全解决方案 task gate 通过；当前仅被范围外 ToyConclusionPersistence 两处缺失 `ITransactionToken` 阻断
- [x] 运行 Textile unit/contract/PostgreSQL integration、architecture/contracts profile 与 Python 全量测试
- [x] 运行严格规格/来源/影响/ready/历史/双 generate/check 门禁
- [x] 核对所有变更均位于 Story allowed_paths，旧 Textile v1 字节无变化且 `git diff --check` 通过
- **Status:** blocked

### Phase 5: cross-task unblock handoff

- [x] 把当前 DEV-028 工作树切到独立 `codex/dev-028-textile-runtime` 分支并创建可审查保存点
- [ ] 从干净 main 建立 DEV-027 修复分支与独立 planning 目录
- [ ] 以 SemVer 后继 Story 修正真实 allowed_paths，完成 Toy 修复和验收
- [ ] 返回 DEV-028，重跑 full-solution task gate 并完成交付
- **Status:** in_progress

## Constraints

- 不编辑 `docs/AI原生第三方产品检测LIMS产品需求文档.md` 或 `generated/spec/**`。
- OD-035 永久属于 DEV-005，不得复用或改写。
- ATC-TEX-002 已在 ATC-TEX-003 的批准证据中明确跳过；新运行时工作使用新的 Story ID。
- 已发布迁移和完成任务证据只追加。
- 未获得新的真实灯塔/生产批准证据，不宣称纺织成为 R1 唯一生产试点。

## Errors Encountered

| Error | Attempt | Resolution |
|---|---:|---|
| 旧 DEV-028 计划拟创建 OD-035，但该 ID 已于 DEV-005 分配 | 1 | 永久放弃复用；以新稳定 ID OD-036 表达纺织运行时边界 |
| 预检 `ready --story ATC-TEX-004@1.0.0` 返回 Story 不存在 | 1 | 作为编码前预期阻断证据；先创建并验证新 Story |
| PowerShell 中的 `rg` 引号/正则组合未匹配 applicability 并以 1 退出 | 1 | 不重复该命令；改为逐 JSON 解析 activation 字段 |
| 首次 strict validate 对 OD-001、BUS-TEX-001/002/003、AC-TEXTILE-001 多个 approved 版本报警并退出 2 | 1 | 核对无 Seal 后，仅把旧 v1 生命周期转为 deprecated 并指向 v2；旧语义内容不改写 |
| 读取猜测路径 `spec/specgen.config.json` 失败（实际配置不在该路径） | 1 | 不依赖猜测路径；直接依据已读取的 validation/snapshot 实现和仓库实际文件工作 |
| 将 OD-001@1.0.0 转为 deprecated 后，校验器拒绝 decided + 非 approved 组合，连带 impact/ready/history 阻断 | 1 | 立即回滚 OD-001 v1 生命周期元数据；检查 decision_state 合法状态，禁止修改校验器绕过 |
| 首次 Textile unit RED 编译混入缺失 `using Xunit` 的测试自身错误 | 1 | 添加显式 Xunit using 后重跑，确保 RED 只指向缺失生产类型 |
| 首次 Textile HTTP RED 在到达路由前被主干 ToyConclusionPersistence 的缺失 `ITransactionToken` 编译错误阻断 | 1 | 确认该类型全仓无定义后，不修改越界 Toy；改用只引用 Textile 模块的 TestServer 契约测试 |
| 模块级 TestServer 首次编译使用已弃用 `WebHostBuilder`，ASPDEPR004 被 warnings-as-errors 阻断 | 1 | 改用当前 `HostBuilder.ConfigureWebHost(...UseTestServer)` 模式后重跑纯 RED |
| 首次 PostgreSQL RED 有两处状态端口调用未传 xUnit cancellation token，xUnit1051 阻断 | 1 | 为两个 EvaluateAsync 调用传入 `TestContext.Current.CancellationToken` 后重跑纯 RED |
| 首次 Textile 基础设施编译缺少 `OpenLIMS.Contracts.Platform` using，无法解析 ServerModuleDescriptor | 1 | 在 TextileModule 添加正确公共 contract using |
| TextileRuntimeService 防御性 `request?.` 导致领域调用前 nullable CS8604 | 1 | 在 Calculate 入口显式 null 失败关闭后再调用领域规则 |
| Textile 集成测试编译成功后因未设置 `OPENLIMS_TEST_POSTGRES_CONNECTION` 8/8 停止 | 1 | 查找并验证仓库既有隔离 PostgreSQL 16 测试实例，显式设置环境变量后重跑，不跳过 |
| 尝试用 `D:\pgtest\pgsql\bin\psql.exe` 探测 55442，但精简分发没有该客户端 | 1 | 已从进程/监听/pg_hba 确认 PG16 trust 实例；改用集成测试自身 Npgsql 直接验证 |
| 组合搜索最后一段在 Python repository test 中未找到既有模块/OpenAPI断言，`rg` 以 1 退出 | 1 | 前两段宿主/架构结果有效；直接读取现有测试结构并新增专用断言，不重复无匹配搜索 |
| 仓库测试证明旧 r1 snapshot 逐哈希冻结 BUS-TEX/AC v1，生命周期改为 deprecated 会破坏不可覆盖基线 | 1 | 恢复 v1 原字节；删除未封存 v2 草案，改用新稳定 ID BUS-TEX-006/007/008 与 AC-TEXTILE-004；保留首份 snapshot 并追加最终快照 |
| 新 Textile 架构守卫把 C# `scope.LegalEntityId` 子串误判为 scope 私有 schema | 1 | 删除原始源码子串检查，保留既有 SQL schema 正则逐匹配断言 |
| 全解决方案 `restore --locked-mode` 因 API/Worker 新增 Textile 项目引用后下游测试项目锁文件未刷新而返回 NU1004 | 1 | 不重复锁定还原；先用 `restore --force-evaluate` 机械更新任务卡允许的 `tests/**/packages.lock.json`，再重跑 locked-mode |
| `restore --force-evaluate` 重写全部锁文件为 CRLF，Git 将无内容差异的文件标为修改；环境无 `dos2unix` | 1 | 任务卡允许所有 lock 路径；按 `.gitattributes eol=lf` 对本次触碰的 lock 文件做纯字节 CRLF→LF 规范化，只保留 17 个真实依赖图差异 |
| 搜索格式门禁时把 PowerShell 不支持的字面路径 `Directory.*` 传给 `rg`，命令以 os error 123 退出 | 1 | 已读取 `scripts/verify.ps1`，仓库无显式格式 gate；不重复错误路径，改为对 Git changed/untracked C# 文件直接运行 `dotnet format --verify-no-changes --include` |
| changed-file `dotnet format --verify-no-changes` 发现 Worker `Program.cs` 导入顺序不符合规则 | 1 | 用相同 `dotnet format --include` 仅机械格式化该允许路径，再对全部 15 个 changed C# 文件复验 |
| Python 全量 unittest 42 个中 1 个失败：v1 规格全集断言的 `approved_delivery_v1_refs` 漏掉已批准 Toy 交付与本次 Textile 运行时 10 个引用 | 1 | 规格/生成门禁均已通过；核验这 10 个对象的 approved 状态和用户批准证据后，在允许的仓库契约测试中补齐显式集合并复跑全量 Python |
| 首次 PowerShell 批量核验 10 个 v1 引用的单行嵌套脚本括号不平衡，解析失败且未读取/修改仓库 | 1 | 不重复嵌套管道写法；改用简单 `foreach` 多行脚本，逐对象收集后统一断言 |
| 逐对象核验发现 `AC-TOY-002@1.0.0` 已 approved 但自身没有内联“用户”批准证据；证据位于精确依赖它的 approved `ATC-TOY-004@1.0.0` 顶层 | 1 | 不改 Toy 规格、不豁免证据；仓库契约对该唯一情况显式追踪 owning Story，要求 Story approved、精确依赖 AC 且含用户批准证据 |
| 任务卡示例命令使用 `pwsh`，当前 Windows 环境未安装 PowerShell 7，命令在脚本启动前失败 | 1 | 不重复不可用命令；用系统 `powershell.exe -NoProfile -ExecutionPolicy Bypass -File` 执行同一 `verify.ps1` |
| 系统 `powershell.exe` 启动脚本后从 PATH 解析到 .NET SDK 9.0.305，无法满足 `global.json` 10.0.302 | 1 | 已验证私有 `C:\Users\Administrator\.dotnet` SDK 10 可用；为子进程前置该目录后重跑同一脚本，不修改 global.json 或门禁 |

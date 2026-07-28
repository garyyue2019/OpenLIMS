# Progress Log: DEV-028

## Session: 2026-07-28 runtime enablement

### Phase 1: governance and READY task card

- **Status:** in_progress
- 用户明确回复“启用纺织运行”，授权从契约切片推进到运行时模块。
- 恢复活动计划时发现 `.planning/.active_plan` 仍指向已完成 DEV-026；已切换到 DEV-028。
- 起始门禁通过：validate 189/389、source current、impact empty；新 Story 尚不存在，ready 预检按预期阻断。
- 已纠正旧计划：不复用 OD-035，不复活已跳过的 ATC-TEX-002；以 OD-001 MAJOR 后继版本和新 Story 表达本次变更。
- 规格设计选定最小生产化切片：DEV-028 只覆盖样品需求/CuttingPlan；调湿与超差继续保持契约层，等待后续独立 Story。
- 一次 applicability 文本检索因 PowerShell/正则转义无匹配而退出 1；未修改仓库，后续改用 JSON 解析。
- 已追加 OD-001@2.0.0、BUS-TEX-001/002/003@2.0.0、AC-TEXTILE-001@2.0.0 与 ATC-TEX-004@1.0.0；`ready` 返回 READY。
- 首次 strict validate 正确识别 6 个 MAJOR 新规格，但因发布基线尚未显式选中五个 v2 后继版本而退出 2；旧 approved 版本保持不可变，下一步更新受控基线。
- 阅读校验器后确认 strict warning 按同一 ID 的 approved 数量直接计算；snapshot 不消除警告。旧版本若未被 Seal 封存，应按生命周期转为 deprecated；若已封存则停止并改用显式发布基线策略。一次猜测配置路径读取失败，无仓库变更。
- `verify-history` 通过且仓库无 Seal；已保持五个 v1 业务内容不动，仅把生命周期转为 deprecated、指向 v2，并把这些路径纳入 Story allowed_paths。
- 严格校验拒绝 `OD-001@1.0.0` 的 decided + deprecated 组合，导致 impact/ready/history 同步阻断；这是有效门禁。下一步先回滚该决策生命周期变更，再选择 schema 支持的后继表达。
- 已恢复 OD-001@1.0.0 原样；删除不兼容的 OD-001@2.0.0 草案，改以新稳定决策 OD-036@1.0.0 拥有纺织运行时实现/受控验证边界，唯一生产试点仍由 OD-001@1.0.0 拥有。
- 治理门禁最终通过：strict validate、source-status、impact、ready、history 全绿；generate 写入 12 个派生文件，check 通过，并创建不可覆盖 snapshot。

### Phase 2: test-first contract and domain extension

- **Status:** in_progress
- 开始读取 Toy 运行时与平台公共端口作为结构参考；将先建立 RED 测试，再实现 Textile 生产代码。
- 已确认 Textile 目前仅有契约项目；选定独立模块 + unit/contract/integration 三层测试结构，只依赖 Platform 公共能力，不引入 Quantity/Allocation 私有实现或项目引用。
- 已对齐 Toy 的可信上下文、精确 capability 授权、事务/审计/Outbox、advisory lock、expectedCurrentVersion 和状态端口模式；Textile 将以自身契约命名复用这些平台模式。
- 首次 unit RED 除预期缺失运行时类型外还暴露测试文件漏引入 Xunit；已修复测试导入，待重跑纯 RED。
- 纯 RED 重跑只剩缺失 Textile 运行时契约/领域类型；追加兼容 runtime contract 和 `TextileRuntimeDomain` 后，领域测试 6/6 通过。
- 添加四操作 HTTP 契约测试后，首次执行在宿主编译阶段被主干 ToyConclusionPersistence 缺失 `ITransactionToken` 阻断，尚未到达预期 404 路由 RED；该文件不在本 Story 范围，将先诊断构建基线。
- 确认 `ITransactionToken`/`NpgsqlTransactionToken` 全仓无定义；将 Textile HTTP 测试改为模块级 TestServer，保持 Toy 文件不动。
- 模块级测试首次使用弃用 WebHostBuilder 被 warnings-as-errors 拦截；已改为 HostBuilder + UseTestServer，避免测试自身噪声。
- 模块级 HTTP 纯 RED 只剩缺失 `TextileEndpoints`；实现四路由、鉴权要求、关联 ID 回传和稳定 problem 映射后，Textile contract suite 28/28 通过。

### Phase 3: runtime module

- **Status:** in_progress
- 进入 PostgreSQL/服务实现；将先使用平台当前 ambient transaction 结构建立权限、并发、恢复、审计/Outbox 和不可变性 RED 测试。
- 已确认平台当前事务模式为 `IPostgresTransactionAccessor` + 同事务 audit/outbox；Textile Store 将只使用该公共 accessor 访问 `textile.*`。
- PostgreSQL RED 初次编译同时出现预期缺失 Module/Migrator 和两处 xUnit cancellation 分析器错误；已修正测试 token，待确认纯基础设施 RED。
- PostgreSQL RED 已纯化为缺失 TextileModule/TextileMigrator；已固定迁移、ambient Store、服务失败审计和状态端口实现模式。
- 新增 migration/module/auth/telemetry/store/service 后首次编译仅缺 Platform contract using；已补齐，继续编译/执行集成测试。
- 下一次编译由 nullable CS8604 拦截 Calculate 入口；已增加显式 null → validation failed guard。
- Textile 模块/迁移/Store/Service 已编译通过；8 个 PostgreSQL 场景仅因当前 shell 缺少测试连接环境变量而停止，下一步定位健康隔离实例后执行。
- 已定位 127.0.0.1:55442 的 D:\pgtest PostgreSQL 16 trust 实例；其精简 bin 无 psql，改由 Npgsql 测试连接直接验证。
- 使用显式 PG16 Npgsql 管理连接后，Textile PostgreSQL 集成测试 8/8 通过：原子事实/审计/Outbox、样品不足、权限、并发、SQLSTATE 55000、失败回滚/重试和状态端口均为绿。
- 已确认 API/Worker 模块目录、项目引用与架构 schema 扫描接线点；开始添加 Textile 宿主/OpenAPI/边界守卫。
- 阅读冻结基线测试后发现 v1 生命周期变更会破坏历史 snapshot 哈希；暂停宿主接线，先恢复 v1 并把运行时规格迁移到新稳定 BUS/AC ID。已创建的首份 snapshot 作为中间证据保留，不覆盖。
- 已恢复旧 v1 原字节，运行时规格改为 BUS-TEX-006/007/008 与 AC-TEXTILE-004；strict validate/ready/history 通过，最终 generate/check/snapshot 完成，impact 归零，R1 冻结测试通过。
- 宿主/静态 OpenAPI/仓库 targeted Python 3/3 通过；架构 18/19，唯一失败为新增测试把 C# scope 属性误判 schema，已收紧为 SQL 正则守卫。
- 收紧后 architecture 19/19 通过。Worker Release 构建成功（0 warning/0 error），API restore 成功；API Release 构建只剩已确认的 ToyConclusionPersistence 两处缺失 ITransactionToken 错误。
- Phase 3 已完成：领域、HTTP、权限、事务、追加式 PostgreSQL、审计/Outbox、遥测、API/Worker 注册和公共状态端口均已落地并通过专项验证，转入 Phase 4。
- 首次全解决方案 `dotnet restore OpenLIMS.slnx --locked-mode` 返回 NU1004：API/Worker 新增 Textile 项目引用后，引用宿主的测试项目锁文件尚未同步；下一步按任务卡允许范围用 `--force-evaluate` 机械刷新，再重跑 locked-mode。
- 全解决方案 `restore --force-evaluate` 成功，随后 `restore --locked-mode` 成功；真实内容差异为 API/Worker、Textile contract 及 14 个直接/传递引用宿主的测试锁文件。NuGet 同时把更多 lock 文件写成 CRLF，需按仓库 `eol=lf` 机械规范化以清除无语义工作树噪声。
- 按 `.gitattributes eol=lf` 完成 lock 文件纯行尾规范化，工作树只保留 17 个真实依赖图差异；并行复跑 Textile unit 6/6、contract 28/28、PostgreSQL integration 8/8、architecture 19/19 全通过，Worker Release 0 warning/0 error。
- 首次 changed-file `dotnet format --verify-no-changes` 只发现 Worker `Program.cs` 导入顺序问题；将做单文件机械格式化后复验全部 15 个 changed C# 文件。
- 已用 `dotnet format --include` 仅调整 Worker import order，全部 15 个 changed/untracked C# 文件的 `--verify-no-changes` 复验通过。
- 完整规格链通过：strict validate 195/389、source current、impact empty、ATC-TEX-004 READY、history passed；两次 generate 均 `written=0 unchanged=131 removed=0`，check passed。Python unittest 41/42，通过之外唯一失败是仓库契约的 v1 approved 显式清单未包含既有 Toy 交付与本次 Textile 10 个批准引用。
- 仓库契约显式纳入遗漏的 10 个 approved v1 对象；`AC-TOY-002` 不作证据豁免，而是验证 approved owning Story `ATC-TOY-004` 的精确依赖与用户批准证据链。Python 全量 unittest 42/42 通过。
- 使用私有 .NET 10 SDK 执行 `verify.ps1 -Profile task -Module textile`：locked restore 通过；full solution build 仅在未改动 ToyConclusionPersistence 第 18/69 行缺失 `ITransactionToken` 失败（0 warnings/2 errors）。Textile 及其所有依赖、测试项目在该 build 中均成功生成；按任务卡边界不修改 Toy。
- 独立 `verify.ps1 -Profile architecture` 通过（19/19）；`verify.ps1 -Profile contracts` 通过，全部匹配测试无失败，Textile contract 28/28。任务脚本之外的专项、架构、契约、规格和 Python 门禁均为绿。
- 最终范围审计：66 个 changed/untracked 路径全部命中 ATC-TEX-004 allowed_paths，违规 0；`git diff --check` 通过；BUS-TEX-001/002/003 与 AC-TEXTILE-001 四个冻结 v1 文件相对 HEAD 字节差异为 0。Phase 4 唯一未完成条件是范围外 Toy 编译缺陷导致的 full-solution task gate。
- 最终交付前复核 `git diff --check` 与 `specgen check` 再次通过；计划准确保留 Phase 4 in_progress，仅等待独立授权修复 Toy 后重跑 full-solution task gate。
- 用户回复“按你建议的做”，授权按顺序隔离 DEV-028、创建 DEV-027 SemVer 后继修复卡、完成 Toy 修复后返回纺织任务。续作开工门禁再次通过：validate 195/389、source current、impact empty、ATC-TEX-004 READY。
- 已从 main 当前提交创建 `codex/dev-028-textile-runtime` 并保留全部工作树内容；下一步在该分支创建可审查保存提交，然后从干净 main 开始 Toy 修复。
- 已在 `codex/dev-028-textile-runtime` 创建 DEV-028 保存提交；66 个文件的纺织规格、实现、测试、生成物与计划证据已脱离 main 安全保存。
- DEV-031 Toy 修复已完整验收并提交为 `3ff16b5`；开始将该提交整合回 DEV-028。
- Cherry-pick 出现预期并行改动冲突：生成物交给 specgen 重建，NuGet lock 交给 restore 重建，Worker/architecture/repository contract 将显式保留 Textile 与 Toy 两侧语义。

## Test Results

| Command | Result |
|---|---|
| `python -m tools.specgen validate` | PASS: 189 versions / 389 source entries |
| `python -m tools.specgen source-status` | PASS: SOURCE CURRENT |
| `python -m tools.specgen impact` | PASS: empty |
| `python -m tools.specgen ready --story ATC-TEX-004@1.0.0` before creation | EXPECTED BLOCK: Story does not exist |
| `python -m tools.specgen ready --story ATC-TEX-004@1.0.0` after governance specs | PASS: READY |
| `python -m tools.specgen validate --strict-warnings` after governance specs | BLOCKED: 5 explicit baseline selections still point to old approved versions |
| strict validate after deprecating v1 successors | BLOCKED: OD-001 decision_state=decided requires status=approved |
| final governance strict validate | PASS: 195 versions / 389 source entries |
| final governance ready | PASS: READY ATC-TEX-004@1.0.0 |
| first governance generate/check | PASS: written=12, check consistent |
| `snapshot --name dev-028-textile-runtime-baseline` | PASS: immutable snapshot created |
| Textile unit tests before runtime types | EXPECTED RED: missing runtime contracts/domain only |
| Textile unit tests after contracts/domain | PASS: 6/6 |
| Textile HTTP contract tests before endpoints | BLOCKED BEFORE RED: unrelated ToyConclusionPersistence missing ITransactionToken |
| Textile module-level HTTP tests before endpoints | EXPECTED RED: missing TextileEndpoints only |
| Textile contract tests after endpoints | PASS: 28/28 |
| Textile integration tests after infrastructure compile | ENV BLOCK: OPENLIMS_TEST_POSTGRES_CONNECTION missing, 8/8 not executed |
| Textile integration tests with isolated PostgreSQL 16 | PASS: 8/8 |
| Architecture tests after Textile guard correction | PASS: 19/19 |
| Worker Release build with Textile module | PASS: 0 warnings / 0 errors |
| API Release build with Textile registration | BLOCKED: pre-existing ToyConclusionPersistence missing ITransactionToken at lines 18 and 69 |
| first full solution locked restore after host project references | BLOCKED: NU1004 in downstream test lock files; requires mechanical force-evaluate refresh |
| full solution force-evaluate restore | PASS; lock graph refreshed |
| full solution locked restore after refresh | PASS |
| Textile unit rerun after lock refresh | PASS: 6/6 |
| Textile contract rerun after lock refresh | PASS: 28/28 |
| Textile PostgreSQL integration rerun after lock refresh | PASS: 8/8 |
| Architecture rerun after lock refresh | PASS: 19/19 |
| Worker Release build after lock refresh | PASS: 0 warnings / 0 errors |
| changed-file dotnet format verification | PASS after one mechanical Worker import-order fix |
| final strict/source/impact/ready/history/double-generate/check | PASS; both generate runs written=0 |
| first full Python unittest after Textile specs | FAIL: 41/42; approved v1 explicit set missing 10 delivered refs |
| full Python unittest after explicit v1 evidence-chain update | PASS: 42/42 |
| `verify.ps1 -Profile task -Module textile` with .NET 10 PATH | EXTERNAL BLOCK: only ToyConclusionPersistence.cs lines 18/69 CS0246 ITransactionToken; Textile projects built successfully |
| `verify.ps1 -Profile architecture` | PASS: 19/19 |
| `verify.ps1 -Profile contracts` | PASS: all matched tests; Textile 28/28 |
| final allowed-path / whitespace / frozen-v1 audit | PASS: 66 paths, 0 violations; diff-check clean; old Textile v1 unchanged |
| DEV-031 independent remediation | PASS: committed as `3ff16b5`; Toy unit 45/45, PostgreSQL 29/29, contract 32/32, Result unit 10/10, Result PostgreSQL 9/9, architecture 18/18, contracts profile, Python 41/41, Release build and Worker migration all passed |
| DEV-031 into DEV-028 cherry-pick preparation | IN PROGRESS: all expected conflicts resolved; specgen regenerated outputs and NuGet force-evaluate restore refreshed locks; cherry-pick not yet continued |
| combined Python repository suite | PASS: 42/42 |
| combined locked restore | PASS with .NET SDK 10.0.302 |
| combined full-solution Release warnings-as-errors build | PASS: 0 warnings / 0 errors |
| combined Textile task gate | PASS: unit 6/6, contract 28/28, PostgreSQL integration 8/8; build 0 warnings / 0 errors |
| combined Toy task gate | PASS: unit 45/45, contract 32/32, PostgreSQL integration 29/29; build 0 warnings / 0 errors |
| combined architecture profile | PASS: 19/19 |
| combined contracts profile | PASS: all matched contract tests, including Textile 28/28 and Toy 32/32 |
| combined final governance chain | PASS: strict validate 196/389, SOURCE CURRENT, empty impact, Textile/Toy READY, history passed, both generate runs `written=0 unchanged=133`, check passed, Python 42/42 |
| Worker migration command audit | Worker composes both `TextileModule` and `ToyModule`; `--apply-module-migration textile` uses `Platform__OrganizationGroupId` and `Platform__PostgresConnectionString` configuration |
| combined allowed-path audit preparation | Final changed-path audit must evaluate the union of `body.allowed_paths` from `ATC-TEX-004@1.0.0` and `ATC-TOY-005@1.0.0` |
| Worker Textile migration command | PASS against local PostgreSQL test instance; exit code 0 |
| combined final allowed-path and whitespace audit | PASS: 63 changed paths, 56 approved patterns, 0 violations; `git diff --check HEAD` clean; no unmerged files |
| final cherry-pick staging audit | PASS: 63 staged paths, 0 unstaged paths, 0 unmerged paths; staged diff check clean |

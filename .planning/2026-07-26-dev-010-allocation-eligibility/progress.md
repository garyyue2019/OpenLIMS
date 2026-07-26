# DEV-010 进度

## 2026-07-26

- DEV-009 合并（`main@9b8328e`）后，用户指示继续 DEV-010，方向为 `ATC-ALLOC-001 任务分配资格`。
- 前置门禁通过：validate（99 规格版本）、SOURCE CURRENT、impact 无影响项。
- ready 检查确认 `ATC-ALLOC-001@1.0.0` 尚不存在。
- 从 `main@4f23cad` 创建分支 `codex/dev-010-allocation-eligibility`，建立独立规划、发现和进度记录。
- 启动 4 路并行侦察：PRD 分配语义、规格清单、平台事务/跨模块组合约束、backlog Epic 定位。
- 4 路并行侦察完成（PRD 语义、规格清单、平台组合约束、backlog 定位），273k tokens、54 次工具调用、0 失败。
- 确认三个关键事实：AC-ALLOC-001 是收款核销不可用作验收锚点（改用 AC-ELEC-003）；PRD 无分配专属未决 OD（OD-035@1.0.0 已把分配列为 deferred_scope，本卡补齐）；嵌套事务抛 PLT.NESTED_TRANSACTION_NOT_SUPPORTED，必须 gate-then-commit。
- Phase 1 完成；Phase 2 等待用户批准 DEV-010 最小业务基线。
- 用户明确批准 DEV-010 业务基线（不可变版本固定分配事实、三端口 gate-then-commit、破坏性互斥阻断、allocation.assign 单一能力、失败关闭状态查询端口、无新增 OD）；Phase 2 完成，Phase 3 开始。
- 已追加 5 项批准源规格（BUS-ALLOC-001~003、AC-ELEC-003、ATC-ALLOC-001 均 @1.0.0）；strict validate 一次通过（104 规格版本），`ATC-ALLOC-001@1.0.0` READY，二次 generate written=0。
- 发现既有规格/代码动作名不一致（规格 TEST_OBJECT_ALLOCATION vs 代码 TEST_ASSIGNMENT）；本卡按代码契约常量调用并在任务卡注明语义等同，已把对齐决策标记为独立后台任务（task_f44a7387），不在本卡内改动既有规格或 Receiving 契约。
- 核实三个被消费端口各自校验既有能力（Receiving 放行批准、scope.approve、quantity.post）；已在任务卡权限节显式声明"不放宽也不复制"，重跑门禁仍 READY。
- 仓库契约测试机械更新（104 规格、任务/feature 集合、@1.0.0 交付集合 +5）后 Python 40/40 通过。
- Phase 3 完成，正式进入 allowed_paths 内实现。
- 已创建 Allocation 契约、领域规则、授权、迁移、持久化、gate-then-commit 服务、状态端口、Endpoint、遥测和模块组合；模块只引用 receiving/scope/quantity 公共契约。
- 已接入 API/Worker 组合根、OpenAPI 路径、Solution 与 verify 脚本（allocation 过滤）；restore 更新 6 个引用 API Host 的测试项目锁图，Release/warnaserror 构建 0 警告 0 错误。
- Allocation 单元 12/12、契约 11/11、集成 11/11（专用数据库 openlims_allocation_test 隔离）、架构 11/11 一次通过；已撰写 docs/domain/allocation。
- Phase 4/5 完成，进入 Phase 6 全量门禁。
- Phase 6 全量门禁通过：strict validate（104 规格版本）、SOURCE CURRENT、HISTORY PASSED、READY、spec check、二次 generate written=0、Python 40/40、locked restore。
- 全解决方案 19 个测试项目 277/277 全部通过（含五组真实 PostgreSQL 集成回归）；allocation 专用数据库隔离从首次全量运行即无跨程序集干扰。
- 路径审计：60 个变更文件全部位于任务卡 allowed_paths，outside_allowed=0；PRD 未修改，generated/spec 只经生成器更新。
- Phase 6 门禁全部完成；按约束等待用户的提交/推送指令。
- 用户指示提交并推送；已提交（60 个文件，全部在 allowed_paths 内，无构建产物），推送到 `origin/codex/dev-010-allocation-eligibility`。
- 经 GitHub API 创建 PR：https://github.com/garyyue2019/OpenLIMS/pull/10，等待远端 CI。
- 两个提交的 Specification governance 与 Application CI（含 Linux PostgreSQL 五模块集成测试）均 success。等待用户合并指令。
- 用户指示合并；PR #10 以 squash 方式合并为 `main@6091510`，远端分支按惯例保留。
- 合并后 main 的 Specification governance 与 Application CI 均 success；本地 main 已快进并切换。main 现包含 10 个已交付切片，DEV-010 全部完成。

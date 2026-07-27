# Findings: DEV-024

## Restored Context

- 当前分支：`codex/dev-024-toy-age-grade-accessibility`，基于 `c721229`。
- 当前工作区包含未提交的 toy 规格、生成物、公共契约、模块实现、API 接线及 unit/contract/integration 测试。
- 任务卡：`ATC-TOY-001@1.0.0`，实现任务 `DEV-024`，readiness 为 `ready`。
- 目标：分离客户年龄声明与实验室年龄判定；判定与可触及性评估追加式版本化；新暴露部件触发机械/化学/标签三范围重评；公开 `IToyAgeGradeStatusPort`。

## Start Gates

- `python -m tools.specgen validate`：通过，168 个规格版本，389 个 PRD 来源条目。
- `python -m tools.specgen source-status`：`SOURCE CURRENT`。
- `python -m tools.specgen impact`：无新增/变更/删除规格、来源漂移或影响项。
- `python -m tools.specgen ready --story ATC-TOY-001@1.0.0`：`READY`。

## Recovery Notes

- 根目录 `task_plan.md`、`progress.md`、`findings.md` 属于更早的产品/PPT工作，不是 DEV-024 的执行记录。
- `.planning/.active_plan` 仍指向 DEV-006；为避免越过任务卡路径，本次不修改该指针。
- 尚未取得前序 AI 的测试结果，必须从任务卡 verification commands 建立可信基线。
- 基线验证发现 `scripts/verify.ps1` 的 `Module` 参数白名单尚无 `toy`，任务卡指定的 toy 验证当前无法启动；任务卡已授权修改 `scripts/verify.ps1` 与 `scripts/verify.sh`。
- 补齐验证脚本后，toy 验证能进入 restore gate，但系统 PATH 中仅有 .NET SDK `9.0.305`；仓库 `global.json` 固定要求 `10.0.302`。
- `C:\Users\Administrator\.dotnet` 提供匹配的 SDK `10.0.302`；临时前置 PATH 后 locked restore 检出引用 API 的既有测试项目锁文件仍是增加 toy 模块前的状态。
- 更新锁文件后 locked restore 通过，解决方案构建仅在 `ToyPersistenceTests.cs` 留下 3 个测试代码错误：`ToyResolution` 类型名两处、xUnit2031 一处。
- 修复测试编译后解决方案构建为 0 warning / 0 error；toy unit 14/14、contract 13/13 通过。toy integration 11 项均因缺少 `OPENLIMS_TEST_POSTGRES_CONNECTION` 在同一环境检查点失败，尚未执行数据库断言。
- 复用本机隔离 PostgreSQL `127.0.0.1:55442` 后，Npgsql 可连接并创建/使用 toy 测试数据库；单项探测在首条声明处暴露真实 `TOY.EXPECTED_VERSION_CONFLICT`。
- 根因：`LoadProductAsync` 把产品注册事实计入 overview version（起始为 1），而 `MutateAsync` 在首命令内部先注册产品、再用注册后的版本校验调用方的 `expectedCurrentVersion=0`。并发令牌应对比命令开始时的聚合版本：不存在时为 0。
- 产品注册表本身保存了注册人、时间、事件与 correlation ID，也计入聚合版本；集成契约要求其同事务写 `REGISTER_TOY_PRODUCT` audit intent，但不发布额外 outbox 事件。前序实现漏了该审计写入。
- 修复后 toy PostgreSQL 集成测试 11/11 通过，覆盖声明/判定分离、冻结与改判、三阶段评估、新暴露三范围触发、无新增边界、结清、版本固定、授权拒绝以及平台证据失败回滚。
- 全量锁文件重算后，`src/host/worker/.../packages.lock.json` 一度被 Git 状态标为修改，但内容 diff 为零；刷新索引后标记消失。其余真实变更全部匹配任务卡 `allowed_paths`，`git diff --check` 通过。
- 最终 Python 契约测试发现 `tests/test_repository_contract.py` 三处精确基线未随 4 个 toy 规格更新：总数 164→168、生成任务集合新增 ATC-TOY-001、批准交付引用集合新增 BUS-TOY-001/002、AC-TOY-001、ATC-TOY-001。
- `scripts/verify.ps1 -Profile all` 已通过 locked restore、全解 0 warning build、平台/契约门禁、前端 lint/typecheck、47 个前端单测和生产构建，随后因本机没有 `docker` 命令停止；不是实现或断言失败。
- 使用隔离 PostgreSQL 直接执行 `dotnet test OpenLIMS.slnx -c Release --no-build --no-restore`，全部 .NET 测试项目通过，包含既有模块、契约、集成、平台链路 E2E 与 toy 测试。
- 最终 `git diff --check`、`specgen check`、Story READY 与 allowed-path 审计全部通过；真实越界路径为零。

## Follow-up Backlog Audit

- 用户授权继续处理后续任务，但仍以结构化 Story 的批准状态、readiness、精确依赖和 `allowed_paths` 为边界；BLOCKED 任务不自行补业务默认值。
- 2026-07-27 续作前总门禁再次通过：validate 168/389、SOURCE CURRENT、impact 全空。
- 结构化 Story 中的实施任务只到 DEV-024；DEV-002～023 已有主干交付提交，DEV-024 是当前最后一张已批准 READY 卡。
- `ATC-TEX-002` 已按用户既有决定跳过；`ATC-RETEST-001` 只存在于建议 backlog，尚无结构化 Story。
- toy 后续最小依赖顺序建议为：先 OPS-TOY-004/006（TestUnit 危险域、互斥破坏、样品需求与技术批准），再 OPS-TOY-007（LabelReview 版本失效与重审）。这些尚无 BUS/AC/Story 结构化规格，不能直接编码。
- OPS-TOY-005（多 TestUnit 汇总结论）明确依赖 OD-034；当前只有 `OD-034@0.1.0`，状态 `proposed/open`，因此必须阻断，不能自行默认结论语义。
- Git remote 为 `origin=https://github.com/garyyue2019/OpenLIMS.git`；当前环境没有 `gh` 命令。

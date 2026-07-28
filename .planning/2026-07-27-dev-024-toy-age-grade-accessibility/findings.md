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

## Continuation Findings: 2026-07-27

- 会话恢复脚本报告前序上下文未完全写入计划；已依据 Git 与 GitHub 实时状态补记。
- PR #24 的三项 GitHub checks 均已完成并成功；PR 状态为 open、mergeable=true、mergeable_state=clean。
- 本地工作树无未提交变更，分支与 `origin/codex/dev-024-toy-age-grade-accessibility` 同步。
- 后续业务项仍无获批 READY Story；可继续完成的是 DEV-024 合并和 `proposed`/`in_review` 规格草案，不能在没有人工批准的情况下进入实现。
- 当前浏览器会话已保留 GitHub 登录态，并存在 PR #24 页面标签，可直接用于合并操作。
- 旧标签页能读取 DOM 但点击在页面执行层超时；重新导航后页面进入正常的 merge-status 重载流程，应等待状态组件稳定后重新定位按钮。
- 当前浏览器 tabs 接口支持 `new/selected/list/content/get`，不支持 `open`；已改用受支持的 `tabs.new` 创建干净标签页。
- 新标签页 id=2 已加载同一 PR，登录态、3/3 checks、无冲突和 `Squash and merge` 均正常；可在干净执行上下文重试一次交互。
- 干净标签页点击成功，GitHub 已显示最终确认页；提交标题为 `feat(toy): deliver DEV-024 age grade accessibility (#24)`，未添加额外描述。
- GitHub API 与 Git fetch 双重确认合并完成：PR #24 merged=true，merge SHA 为 `3981eba4096bf8eb165713720f9f7d9c200b29ee`，`origin/main` 指向该提交。
- 本地 `main` 已快进到 `3981eba`；续作 planning 记录已独立提交，保持与 DEV-024 业务合并提交分离。
- 最新 `main` 的规格开始门禁通过：168 个规格版本、389 个来源条目、SOURCE CURRENT、impact 为空。
- OPS-TOY-004/005/006/007 当前只出现在 PRD/source-baseline 和 ATC-TOY-001 non-goals 中，`spec/requirements` 尚无对应结构化对象。
- OPS-TOY-005 必须继续排除在可实施草案外，因为 OD-034 只有 `OD-034@0.1.0` 且 `proposed/open`；现有报告门禁也依赖这一阻断语义。
- PRD 已有稳定 `AC-TOY-002`，其原文同时覆盖：事件前后可接触性/照片、互斥破坏 TestUnit 阻断，以及多 TestUnit 汇总结论显示危险域与覆盖依据。该验收不可被缩写成只覆盖 OPS-TOY-004/006 的“通过”版本，否则会掩盖 OD-034 阻断。
- 已交付的 Allocation 模块已有通用破坏性互斥和版本固定分配；玩具草案应复用公共分配端口并新增 TestUnit/危险域/平行/序列语义，不直接访问 allocation 私表。
- 已交付 Quantity 模块已有不可变流水、计量维度和并发预留；玩具样品需求计算只应产出版本固定需求及数量预留请求，不复制数量余额逻辑。
- 现有 Labeling 模块服务于收样标签打印/扫描，不包含产品包装、说明书或营销声明审查。OPS-TOY-007 的行业语义宜由 toy 模块拥有，通过公共契约协作，避免把样品条码模块扩成产品合规主数据模块。
- 草案拆分为：DEV-025（BUS-TOY-003/004 + AC-TOY-003，TestUnit/样品需求）、DEV-026（BUS-TOY-005 + AC-TOY-004，LabelReview）、DEV-027（BUS-TOY-006 + 完整 AC-TOY-002，多 TestUnit 结论显式阻断）。
- 完整 AC-TOY-002 保留 PRD 汇总结论要求并依赖 OD-034；DEV-025 使用单独 AC-TOY-003，因此未来可在不伪装结论已决定的情况下独立批准。
- 所有新规格均为 `0.1.0 proposed`，没有 approval_evidence、没有被 AI 标记 approved；ready 输出精确列出人工评审边界。
- 生成器为 3 张新 acceptance、3 张新 Story 及目录/追溯文件写入 18 个派生文件，二次生成稳定为 `written=0`。
- 仓库契约要求每张 Story（即使 blocked/proposed）显式依赖 `OD-002@1.0.0`，以维持服务端组织上下文与禁止客户端选择集团的全局边界；ATC-TOY-004 需补齐。
- 生成 readiness-report 明确区分：DEV-025/026 只待 proposed 规格人工批准；DEV-027 同时受 OD-034 open/proposed、完整 AC-TOY-002 和前序 DEV-025 阻断。
- `tests/test_repository_contract.py` 只更新三个精确事实：规格版本 168→178、feature 60→66、生成任务新增 ATC-TOY-002/003/004；其余门禁保持严格。
- `docs/domain/toy/follow-up-spec-review.md` 列出 DEV-025/026 的具体评审问题和 DEV-027 的 OD-034 exit criteria，且明确该文档不是 PRD 或批准证据。
- GitHub 新 PR 页面已识别 `main...codex/toy-follow-up-spec-drafts`，标题默认正确、分支可自动合并；应选择 Draft PR，避免把草案合并动作误读为业务批准。
- Draft PR #25 已由 GitHub API 复核：open、draft=true、base main、head `f1a7c21`。该 PR 是评审载体，不是 approval_evidence，也不授权 DEV-025～027 实现。

## Approval Findings: 2026-07-28

- 用户现已成为 DEV-025/026 的最终人工批准主体；批准对象是 PR #25 当前的 BUS-TOY-003/004/005、AC-TOY-003/004 和 ATC-TOY-002/003 草案语义及评审清单中的相关选择。
- 为遵守版本历史，批准落地方式是新增 `1.0.0 approved` 后继规格并在 Story 中记录本次用户声明，不修改 `0.1.0 proposed` 文件。
- DEV-027 的 BUS-TOY-006、完整 AC-TOY-002 和 ATC-TOY-004 未被本次声明批准；OD-034 仍为 proposed/open。
- PR #25 在上一会话最终状态为三项 checks 全部 success、draft=true、mergeable=true/clean；本轮变更后必须重新验证。
- 2026-07-28 开始门禁确认规格基线仍为 178/389、来源当前、impact 为空；PR #25 仍处于 open/draft/mergeable clean。
- 当前 specgen CLI 没有 promotion 子命令；仓库文档明确要求受控流程创建 SemVer 后继版本，因此必须保留 proposed 0.1.0 并新增 approved 1.0.0。
- `scaffold` 只能按 kind/id/version 创建空骨架，不能继承前一版本。为避免手工漏掉长 Story 语义，批准后继应由草案 JSON 做确定性字段变换，再由 `apply_patch` 独占文件写入。
- 批准变换结果满足目标边界：DEV-025/026 的 requirement、acceptance 和 Story 形成精确 1.0.0 approved 闭包，Story readiness=ready；DEV-027 没有被连带批准。
- 批准规格完成后总版本数 185、生成文件 125；仓库契约只更新明确计数、两张 1.0 task/四个 feature 和七个 approved delivery 引用，未放宽任何断言。
- PR #25 最终三项 checks 全部 success，merged=true，主干提交 `26bf6f3`；DEV-025/026 已具备从主干开始实施的正式规格基础。
- 任务卡授权的 planning 路径分别是 `.planning/2026-07-27-dev-025-toy-test-unit-sample-demand/**` 与 `.planning/2026-07-27-dev-026-toy-label-review/**`；后续不能继续改本 DEV-024 计划文件。

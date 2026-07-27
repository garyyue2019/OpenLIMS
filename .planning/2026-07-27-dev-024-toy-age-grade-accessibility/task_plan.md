# Task Plan: DEV-024 玩具年龄分级与可触及性评估

## Goal

接续当前未提交工作，完成 `ATC-TOY-001@1.0.0` 的实现、验证与交付；保留前序 AI 的有效改动，只在任务卡 `allowed_paths` 内修复缺口。

## Current Phase

Phase 8: 后续规格工作边界与草案

## Phases

### Phase 1: 恢复上下文

- [x] 识别当前分支、未提交变更与任务卡
- [x] 执行开始任务前的规格门禁
- [x] 确认来源无漂移、Story 为 READY、改动路径在授权范围内
- **Status:** complete

### Phase 2: 基线验证与差距定位

- [x] 执行任务级 toy 验证
- [x] 执行架构验证与 specgen check
- [x] 审查失败项及关键实现覆盖
- **Status:** complete

### Phase 3: 定向修复

- [x] 只修复验证或审查确认的缺口
- [x] 同步必要的正向、反向、边界、权限、并发、恢复和审计测试
- [x] 复跑受影响验证
- **Status:** complete

### Phase 4: 完成前全量门禁

- [x] 运行严格规格、来源、历史、生成与检查门禁
- [x] 验证第二次 generate 为 `written=0`
- [x] 运行 Python 全量单元测试及仓库要求的 .NET 验证
- [x] 确认所有改动均位于 `allowed_paths`
- **Status:** complete

### Phase 5: 交付

- [x] 汇总实现、测试证据和剩余风险
- [x] 未经明确授权，不提交、不推送、不创建 PR；保留已验证工作区供用户决定
- **Status:** complete

### Phase 6: 后续任务盘点与交接

- [x] 枚举尚未交付的结构化 Story 与实施任务 ID
- [x] 对候选范围核对 readiness 前置条件，区分可起草项与明确阻断项
- [x] 确认依赖顺序、分支/提交边界和可连续执行范围
- [x] 确认 DEV-024 后不存在已批准 READY 卡，记录下一卡需先起草并由用户评审
- **Status:** complete

## Constraints

- 不直接编辑 `generated/spec/`；仅由 specgen 生成。
- 不修改来源 PRD。
- 不自行补充 BLOCKED 业务默认值。
- 不创建 Seal、tag、GitHub Release 或部署。

## Errors Encountered

| Error | Attempt | Resolution |
|---|---:|---|
| 根目录计划与当前 DEV-024 无关，`.planning/.active_plan` 仍指向 DEV-006 | 1 | 使用任务卡允许的独立 DEV-024 计划目录，不改旧记录与活动指针 |
| 当前环境找不到 `pwsh` 可执行文件 | 1 | 改用当前 Windows PowerShell 直接执行同一 `scripts/verify.ps1`，不改变验证范围 |
| `verify.ps1 -Module toy` 被参数白名单拒绝 | 1 | 检查 PowerShell/Bash 验证脚本并补齐 toy 模块映射后重试 |
| 仓库要求 .NET SDK `10.0.302`，当前 PATH 的 SDK 只有 `9.0.305` | 1 | 查找工作区或 Codex 提供的匹配 SDK；不修改 `global.json` 降低工具链要求 |
| locked restore 检出引用 API 的既有测试项目锁文件缺少新 toy 项目引用 | 1 | 使用正确 SDK 执行一次 `dotnet restore --force-evaluate` 机械更新任务卡允许的锁文件，再恢复 locked-mode 验证 |
| toy 集成测试构建失败：2 个 `ToyResolution` 未解析，1 个 xUnit2031 | 1 | 对照契约类型与同文件断言风格做最小测试修复后重跑任务验证 |
| toy 集成测试 11 项因缺少 `OPENLIMS_TEST_POSTGRES_CONNECTION` 全部在环境检查处失败 | 1 | 查找仓库既有测试数据库约定与本机容器状态，提供连接变量后重跑；不跳过测试 |
| 现有 `D:\pgtest` 集群无 `psql.exe`，无法用 CLI 预探测 | 1 | 改用仓库测试自身的 Npgsql 驱动连接；凭据仅注入当前进程且不回显 |
| 数据库探测测试进入业务逻辑后，首条客户声明抛出 `TOY.EXPECTED_VERSION_CONFLICT` | 1 | 审查产品当前版本派生与命令 expected version 校验，修复生产逻辑并补回归 |
| 首条声明流程通过后，平台审计意图预期 3 条、实际 2 条 | 1 | 核对产品注册事实的审计要求；若缺失则在注册同事务补 audit intent，不增加非契约 outbox 事件 |
| Python 全量测试 3 项仓库契约基线仍停在新增 toy 规格前 | 1 | 按现有严格集合断言补规格总数、生成任务名与已批准交付引用；不放宽为模糊匹配 |
| 记录上述 Python 失败的首次计划补丁因中文空格上下文不匹配未应用 | 1 | 读取计划文件尾部后用精确上下文重新追加，未影响代码 |
| 更新任务集合后，同一测试继续执行并显出 feature 精确总数仍为 58 | 2 | 两个新 toy feature 使总数变为 60；同步精确计数后重跑 |
| 仓库级 `Profile all` 在通过 .NET/前端门禁后因本机无 Docker 停止 | 1 | 记录环境限制，直接运行解决方案全部 .NET 测试并核对 Docker 阶段；不弱化验证脚本 |
| 当前环境无 `gh` 命令 | 1 | Git 提交/推送可继续；创建或管理 PR 需改用浏览器或 GitHub API |
| 首次追加续作计划时 progress 锚点重复，补丁未应用 | 1 | 读取三个计划文件尾部并按精确 EOF 上下文分别追加；未影响仓库内容 |
| 首次按可访问角色点击 GitHub `Squash and merge` 超时 | 1 | 不重复同一点击；重新检查页面状态并改用页面当前元素引用或更精确定位 |
| 改用精确文本定位点击仍在同一 CDP Runtime.evaluate 层超时 | 2 | 判断标签页交互上下文陈旧；下一次重新导航/刷新后再取新元素，不继续复用旧 locator |
| 重新导航并等待状态稳定后，第三次新 locator 点击仍在 CDP Runtime.evaluate 层超时 | 3 | 停止在该标签页重试；改用同一登录浏览器的新标签页，若仍失败则不绕过 GitHub 合并审计而报告外部 UI 阻断 |
| 尝试 `browser.tabs.open` 新建标签页时 API 不存在 | 1 | 检查当前浏览器绑定公开的 tabs/user 方法，使用受支持的新建标签方式；不猜测重复调用 |
| 带未提交 planning 续作记录切换 `main` 被 Git 阻止，后续误在特性分支尝试 ff-only 也因分叉失败 | 1 | 不丢弃记录；先把仅 planning 的自有变更提交为临时本地提交，再同步 main 并 cherry-pick 该提交 |
| 新增草案后 Python 仓库契约 3 项失败：规格总数/生成任务精确基线未更新，ATC-TOY-004 缺 OD-002 组织上下文依赖 | 1 | 保持严格断言；补精确基线与缺失依赖后重新生成并重跑，不放宽测试 |

## Continuation: 2026-07-27 全部后续任务收尾

### Phase 7: 合并 DEV-024

- [x] 恢复前序会话、分支、PR 与 CI 上下文
- [x] 确认 PR #24 三项检查全部成功且可无冲突合并
- [x] 以 Squash and merge 合并 PR #24
- [x] 同步本地 `main` 并核实合并提交
- **Status:** complete

### Phase 8: 后续规格工作边界与草案

- [x] 在最新 `main` 上重跑规格开始门禁
- [x] 复核 OPS-TOY-004/006、OPS-TOY-007、OPS-TOY-005 与 OD-034 的来源、依赖和开放决策
- [x] 按仓库现有版本与状态惯例起草可评审的 BUS/AC/Story；所有新稿保持 `proposed`/`in_review`，绝不自批 `approved`
- [x] 对每张草案运行适用的 validate/source-status/impact；记录仍需人工批准或业务决策的阻断项
- **Status:** complete

### Phase 9: 最终交付

- [x] 汇总已合并实现、草案文件、验证证据和剩余人工决策
- [x] 确保没有越过 Story `allowed_paths`、没有直接编辑 `generated/spec/`、没有改写已封存历史
- [ ] 提交、推送草案分支并创建 Draft PR 供人工评审
- **Status:** in_progress

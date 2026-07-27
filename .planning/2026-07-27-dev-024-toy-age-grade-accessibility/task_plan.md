# Task Plan: DEV-024 玩具年龄分级与可触及性评估

## Goal

接续当前未提交工作，完成 `ATC-TOY-001@1.0.0` 的实现、验证与交付；保留前序 AI 的有效改动，只在任务卡 `allowed_paths` 内修复缺口。

## Current Phase

Complete

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

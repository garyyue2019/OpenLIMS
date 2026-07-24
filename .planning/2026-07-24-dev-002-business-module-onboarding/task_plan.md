# DEV-002 Business Module Onboarding

## Goal
在不引入任何检测业务语义或共享 SaaS 多租户的前提下，为 OpenLIMS 建立可验证的后端、Worker、Web、迁移和 CI 业务模块接入通道。

## Current Phase
Phase 4 — Full Verification and Delivery

## Phases

### Phase 1: Task Card and Architecture Baseline
- [x] 创建并验证用户批准的 `ATC-PLT-003@1.0.0` / `DEV-002` 任务卡
- [x] 运行 `ready` 并冻结 allowed paths、非目标和验收命令
- [x] 盘点 Host、Worker、Web、迁移和架构门禁的最小改造点
- **Status:** complete

### Phase 2: Parallel Implementation
- [x] 后端模块组合契约、Host/Worker 接入和测试夹具
- [x] 前端功能清单、路由/导航组合和重复冲突测试
- [x] 架构门禁、跨平台验证脚本和 CI 接入
- **Status:** complete

### Phase 3: Integration and Hardening
- [x] 集成并发工作，修复契约冲突
- [x] 补齐正向、反向、边界、权限、并发、恢复和审计回归
- [x] 确认生产应用没有新增业务路由、页面、表或状态机
- **Status:** complete

### Phase 4: Full Verification and Delivery
- [ ] 执行仓库完成门禁与确定性二次生成
- [ ] 审查 allowed paths、工作树和生成目录所有权
- [ ] 提交、推送并创建 PR
- **Status:** in_progress

## Scope Decisions
| Decision | Rationale |
|---|---|
| 用户的“好，那就做 DEV-002”作为本工程任务包人工授权证据 | 用户明确接受了上一轮给出的边界和顺序 |
| 机器任务卡使用 `ATC-PLT-003`，实现任务号保留为 `DEV-002` | `ATC-PLT-001/002` 已在 Backlog 分配给其他平台能力，不能复用 |
| 任务只建设模块接入能力 | 避免在业务 Story 仍 BLOCKED 时补充业务默认值 |
| 测试夹具只能位于 `tests/**` | 防止工程接入任务偷偷产生生产业务能力 |

## Errors Encountered
| Error | Attempt | Resolution |
|---|---:|---|
| `ready --story DEV-002@1.0.0` 返回 Story 不存在 | 1 | 创建符合 ATC 命名约束的 `ATC-PLT-003@1.0.0`，在 body 中固定 `implementation_task_id=DEV-002` |
| Windows 环境没有 `bash`，无法运行 planning skill 的 `init-session.sh` | 1 | 使用 `apply_patch` 创建同结构的隔离计划目录，并保留 `.planning/.active_plan` 为本地选择器 |
| 本机 `pwsh` 命令不在 PATH | 1 | 当前工具本身已运行 PowerShell，改为直接调用 `& .\scripts\verify.ps1` |
| 系统 PATH 只有 .NET SDK 9.0.305，锁定的 10.0.302 无法解析 | 1 | 使用工作区已存在的 `C:\codex_tmp\dotnet-10.0.302` 临时前置 PATH，不修改 `global.json` 或降低版本门禁 |
| 新增第 60 张规格卡后 5 项仓库契约测试仍固定旧数量/清单或拒绝措辞 | 1 | 在未封存的 DEV-002 任务卡 allowed paths 中补充仓库契约测试，并同步 60/20/ATC-PLT-003 期望；将多租户禁令改为测试可识别的明确“禁止”措辞 |
| 首次 allowed-paths 检查把 Git 折叠显示的未跟踪目录 `tests/fixtures/` 当成文件 | 1 | 改用 `git status --porcelain -uall` 展开到实际文件后再匹配任务卡路径 |

## Guardrails
- 不修改 PRD。
- 不直接编辑 `generated/spec/`，只运行生成器。
- 不在任务卡 allowed paths 外修改实现。
- 不添加收样、身份、分析化学、QC、报告或计费业务默认值。
- 不把测试夹具引用进生产 Host。

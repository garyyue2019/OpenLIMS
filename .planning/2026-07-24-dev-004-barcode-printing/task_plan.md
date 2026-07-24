# Task Plan: DEV-004 条码生成与打印

## Goal

在 PR #3 合并后的 `main` 基线上，依据经人工批准且 READY 的精确版本任务卡，完成 DEV-004 条码生成、打印、校验及必要的审计/失败恢复垂直切片，并通过仓库全部交付门禁。

## Current Phase

Phase 6（等待用户明确要求提交与发布）

## Phases

### Phase 1: 合并 DEV-003 前置交付

- [x] 核验 PR #3 Head、检查项与冲突状态
- [x] 使用 Squash merge 合并 PR #3
- [x] 记录主分支合并提交
- **Status:** complete

### Phase 2: 同步基线与执行 DEV-004 前置门禁

- [x] 同步本地 `main` 并确认仅有新计划文件未跟踪
- [x] 创建 `codex/dev-004-barcode-printing` 分支
- [x] 运行 validate、source-status、impact
- [x] 运行 `ready --story ATC-REC-002@1.0.0`
- [x] 核对 Story 依赖、状态、allowed_paths 与未决业务语义
- **Status:** complete

### Phase 3: 获得可执行业务基线

- [x] Story 为 BLOCKED，归并真实未决项并停止编码
- [x] 仅依据用户明确批准的业务决定创建新 SemVer 规格
- [x] 生成并核验新的 READY 任务卡及精确版本依赖
- **Status:** complete

### Phase 4: 实现 DEV-004 垂直切片

- [x] 严格限制在 READY 任务卡 `allowed_paths`
- [x] 实现标签标识生成、打印请求/执行、扫描校验与审计
- [x] 实现重印授权、幂等/并发和打印失败恢复等已批准行为
- [x] 同步提交正向、反向、边界、权限、并发、恢复和审计测试
- **Status:** complete

### Phase 5: 全量验证与证据

- [x] 执行实现相关后端、前端及持久化测试
- [x] 执行仓库完成前六项强制命令
- [x] 二次 generate 确认为 `written=0`
- [x] 核对 changed paths 全部位于任务卡允许范围
- **Status:** complete

### Phase 6: 提交与发布评审

- [x] 更新任务证据和计划记录
- [x] 提交 DEV-004 分支
- [x] 推送并创建 PR
- [ ] 等待 CI 终态并报告结果
- **Status:** in_progress

## Key Questions

1. `ATC-REC-002@1.0.0` 当前是否 READY；若否，具体阻断项是什么？
2. 条码载荷、编码规则、标签层级/内容、打印协议、重印权限、失败恢复和扫描校验语义是否已有批准决策？
3. DEV-004 是否应依赖已交付的 `ATC-REC-001@2.0.0`，从而要求新建 DEV-004 Story 版本？
4. 新任务卡允许修改哪些实现、测试、迁移和运行文件？

## Decisions Made

| Decision | Rationale |
|----------|-----------|
| PR #3 使用 Squash merge | 用户明确要求先合并，且 PR 已全部通过、无冲突 |
| DEV-004 使用独立计划和分支 | 将交付证据、变更范围与 DEV-003 隔离 |
| BLOCKED 时停止实现 | 遵守根目录 AGENTS.md，禁止 AI 自行补业务默认值或批准语义 |
| 不直接编辑 `generated/spec/` | 该目录完全属于规格生成器 |
| 采用用户批准的 DEV-004 业务基线 | 用户于 2026-07-24 明确回复“批准 DEV-004 业务基线” |
| 新建 `OD-031@1.0.0`、`OPS-RECEIPT-002@1.0.0`、`ATC-REC-002@2.0.0` | 旧版本为开放/阻断且依赖陈旧；新行为和依赖属于 MAJOR 语义，禁止原地修改 |

## Errors Encountered

| Error | Attempt | Resolution |
|-------|---------|------------|
| `gh` CLI 不存在 | 1 | 使用用户已登录的 GitHub 浏览器完成语义操作 |
| Playwright 首次点击 `Squash and merge` 超时 | 1 | 重新获取 DOM，改用已验证的可见 DOM 节点完成点击 |
| `ATC-REC-002@1.0.0` 为 BLOCKED | 1 | 停止编码，只进行依赖影响评审并准备人工审批基线 |
| 系统 `dotnet` 只有 SDK 9.0.305，仓库锁定 10.0.302 | 1 | 使用 Codex 工作区提供的锁定 .NET 10 运行时路径，不修改 global.json 或降低版本 |
| LabelingModule 缺少 `ServerModuleDescriptor` 命名空间 | 1 | 添加 `OpenLIMS.Contracts.Platform` 引用后重新编译 |
| 首次测试发现模块契约版本误升和 Receiving↔Labeling 合同循环 | 1 | 保持组合契约 1.0.0；将共享条码编码契约下沉到 Receiving Contracts，移除 Receiving 对 Labeling Contracts 的依赖 |
| 工作区 Node 24.14.0/pnpm 11.9.0 不符合锁定 24.14.1/10.34.5 | 1 | 安装用户级精确 Node 与 pnpm，保持仓库 engines 和 packageManager 不变 |
| Receiving 集成测试准备步骤只执行旧迁移 | 1 | 在测试夹具中按发布顺序追加执行 `20260724_002_label_identity`，不修改旧迁移 |
| Labeling 集成测试从根容器解析 scoped 服务 | 1 | 每个测试创建显式 DI scope，保持事务协调器和 Store 的请求级生命周期 |
| Labeling 查询 SQL 拼接缺少换行 | 1 | 在共享 SELECT 与 WHERE 片段之间显式加入换行并由 PostgreSQL 集成测试验证 |
| 当前终端 PATH 不含 `pwsh`，且常见绝对路径不存在 | 1 | 在当前 PowerShell 进程内直接执行同一 `verify.ps1`，脚本语义和门禁保持不变 |
| `dotnet format --verify-no-changes` 发现本次 Host/Worker/Dispatcher 格式问题和一个既存平台测试导入顺序 | 1 | 修正所有本次变更文件；不越界修改任务卡未授权的既存平台测试源码 |

## Notes

- 用户要求的是单集团独立部署、集团多机构；禁止共享 SaaS 多租户语义进入规格或实现。
- 不扩写无关治理文档；只补齐 DEV-004 能进入 READY 和实施所需的最小业务/工程证据。
- PR #3 合并提交：`19766e795483e7de8cd24d579f3211a95cfda33c`。

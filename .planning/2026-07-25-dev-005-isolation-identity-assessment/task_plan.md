# Task Plan: DEV-005 隔离和身份评估

## Goal

基于已合并 DEV-004 的 `main`，把“统一隔离门禁”和“身份评估”拆解为经人工批准、机器可执行、可验证的垂直任务包；只在精确版本 Story 为 READY 后实现，并保持单集团独立部署、集团多机构边界。

## Current Phase

Phase 5（提交与发布）

## Phases

### Phase 1: 基线与规格门禁

- [x] 确认分支、工作区和 `main` 基线
- [x] 运行 validate、source-status、impact
- [x] 核验 `ATC-REC-003` 与 `ATC-REC-004` readiness
- [x] 核对依赖、未决语义和 allowed_paths
- **Status:** complete

### Phase 2: 形成可执行 DEV-005 业务基线

- [x] 区分隔离门禁与身份评估的职责和依赖顺序
- [x] 归并需要人工批准的最小业务决定
- [x] 创建新 SemVer 规格并生成 READY 任务卡
- **Status:** complete

### Phase 3: 实现垂直切片

- [x] 严格限制在 READY 任务卡 allowed_paths
- [x] 实现服务端隔离门禁、身份评估事实/结论和审计
- [x] 实现必要的前端工作台和公共契约
- [x] 同步正向、反向、权限、并发、恢复和审计测试
- **Status:** complete

### Phase 4: 全量验证

- [x] 运行后端、前端和 PostgreSQL 测试
- [x] 运行 AGENTS.md 六项完成门禁
- [x] 二次 generate 确认 written=0
- [x] allowed-path 审计和最终差异复核
- **Status:** complete

### Phase 5: 提交与发布

- [x] 等待用户明确授权提交和发布
- [x] 提交、推送并创建 PR #5
- [ ] 等待 CI 全部通过（现被任务范围外的既有前端传递依赖高危审计阻断）
- **Status:** in_progress

## Decisions

| Decision | Rationale |
|---|---|
| DEV-005 使用独立分支和计划 | 与已合并 DEV-004 的证据和变更边界隔离 |
| BLOCKED 时不编码 | 遵守 AGENTS.md，不由 AI 自行批准业务默认值 |
| 不直接编辑 generated/spec | 派生目录只由规格生成器维护 |
| 批准 DEV-005 业务基线并使用 ATC-REC-003@2.0.0 | 用户于 2026-07-25 明确批准；ready 门禁已返回 READY |

## Errors Encountered

| Error | Attempt | Resolution |
|---|---|---|
| 系统 `dotnet` 仅有 9.0.305，无法满足 `global.json` 的 10.0.302 | 1 | 使用 `%LOCALAPPDATA%/OpenLIMS/dotnet` 的锁定 SDK 10.0.302；未修改版本门禁 |
| 身份评估三个运行时端点通过，但 OpenAPI 未声明新路径 | 1 | 主机使用显式 OpenAPI 路径清单；已在允许的 API Host 路径内补齐三个操作 |
| 前端客户端测试复用同一 `Response`，第二次读取报 `Body has already been read` | 1 | 改为每次 mock 调用创建新的 Response；生产客户端无需变更 |
| 本机无 PostgreSQL 服务、Docker 或可用 WSL，集成测试无法直接启动 | 1 | 在临时目录使用官方免安装 PostgreSQL 二进制启动隔离测试实例，不修改仓库或系统服务 |
| 临时 PostgreSQL 首次 `pg_ctl -o` 的 Windows 引号传递错误，初始化成功但服务未启动 | 1 | 读取日志后改用临时数据目录的配置文件设置本机端口/监听地址，不重复该参数形式 |
| Python 仓库契约未把生成的 `ATC-REC-003__v2.0.0.md` 加入期望任务清单 | 1 | 在任务卡允许的 `tests/test_repository_contract.py` 同步精确文件名后重跑全部门禁 |
| 同一仓库契约仍使用 DEV-005 前的 Feature 总数 23 | 2 | 更新为精确总数 26，并显式断言三份新增 Feature 名称，未放宽其他检查 |
| 记录上述门禁错误时补丁上下文少了一个空格 | 1 | 使用计划文件中的精确原文重试，未影响业务或测试文件 |
| PR #5 首轮主 `verify` CI 在 `Check frontend` 步骤失败 | 1 | 后端、Receiving/Labeling task profiles 均已通过；正在读取受保护作业日志定位前端 CI 与本地差异 |
| GitHub 作业日志公开 API 要求仓库管理权限，凭据读取也被本地安全边界拒绝 | 1 | 未输出或持久化凭据；依据公开步骤定位到前端检查，并用本地精确 CI 命令复现根因 |
| `pnpm audit --audit-level high` 新报告既有传递依赖高危漏洞 | 1 | 依赖清单/锁文件不在 DEV-005 allowed_paths；不扩卡、不降门禁，记录为需要单独批准的依赖治理阻断 |

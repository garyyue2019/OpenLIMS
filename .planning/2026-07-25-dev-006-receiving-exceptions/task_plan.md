# Task Plan: DEV-006 收样异常与授权决定

## Goal

在已合并 DEV-005 的 `main` 基线上，以最轻可审计治理批准异常分类和决定矩阵，使精确版本任务卡 READY，并交付异常建档、条件接收、拒收和审批权限的最小垂直切片。

## Phases

### Phase 1: 基线与发布闭环
- [x] 推送 CI 轻量治理提交
- [x] 确认 PR #5 全绿并 squash 合并 DEV-005
- [x] 从合并后的 `main` 创建 DEV-006 分支
- **Status:** complete

### Phase 2: 精简业务基线与 READY 任务卡
- [x] 运行 validate、source-status、impact 和旧卡 ready
- [x] 追加批准的异常分类、决定矩阵和需求版本
- [x] 创建批准的 `ATC-REC-005@2.0.0` 并使 ready 返回 READY
- **Status:** complete

### Phase 3: DEV-006 实现
- [x] 仅在任务卡 allowed_paths 内实现异常、决定、权限、审计和工作台
- [x] 同步正向、反向、边界、权限、并发、恢复和审计测试
- **Status:** complete

### Phase 4: 全量验证
- [x] 运行任务卡验证命令和 AGENTS.md 六项完成门禁
- [x] 第二次 generate 确认 written=0
- [x] 核对 allowed_paths 与最终差异
- **Status:** complete

## Decisions

| Decision | Rationale |
|---|---|
| 普通异常由质量负责人批准，危险/污染/安全封存由 EHS 批准 | 最少角色覆盖质量和安全责任边界 |
| 条件接收必须有证据、非空限制和有效期 | 防止默认接收或隐含放开全部下游动作 |
| 发起人不能批准自己的决定 | 用最低成本保留职责分离 |
| UNKNOWN 一律阻断 | 不用业务默认值掩盖未配置场景 |

## Errors Encountered

| Error | Attempt | Resolution |
|---|---|---|
| GitHub HTTPS 默认握手失败 | 1 | 强制 IPv4、HTTP/1.1 和 TLS 1.2 后恢复 |
| 规划技能自动更新 DEV-005 文件导致分支切换被保护 | 1 | 命名 stash 保存笔记，从 `origin/main` 创建正确分支 |
| DEV-006 初稿校验发现两个 activation 枚举无效且 Story 缺 observability | 1 | 改用合法 `ENABLED`，补充低基数可观测性，并复用现有 Receiving 模块路径 |
| DEV-006 首次编译发现 Create 请求的两处可空性告警被 warnings-as-errors 阻断 | 1 | 在服务入口显式拒绝空请求并消除错误的可空传播 |
| 新增 API 契约测试桩引用了测试类私有常量 | 1 | 在测试桩内声明独立固定 ID，保持测试隔离 |
| 本机未安装 `pwsh`，正式 profile 命令无法启动 | 1 | 在当前 Windows PowerShell 中直接调用同一 `verify.ps1` 脚本 |
| Python 精确版本集合仍停在 DEV-005 已批准对象 | 1 | 加入三份 DEV-006 已批准 v1 依赖并补齐人工批准证据，未放宽集合断言 |

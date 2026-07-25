# Progress Log: DEV-005 隔离和身份评估

## Session: 2026-07-25

### Phase 1: 基线与规格门禁

- **Status:** in_progress
- 已从合并后的 `main` 创建 `codex/dev-005-isolation-identity-assessment`。
- 初步定位 `ATC-REC-003` 为隔离门禁，`ATC-REC-004` 为身份评估。
- 尚未开始业务实现。
- `validate` 通过（74 个规格版本、389 个来源条目），`source-status` 为 CURRENT，`impact` 无漂移。
- `ATC-REC-003@1.0.0` 和 `ATC-REC-004@1.0.0` 均返回 BLOCKED；已按 AGENTS.md 停止编码。
- 已区分陈旧依赖与真实未决业务语义，并确认 DEV-006/DEV-007 的职责边界。

### Phase 2: 形成可执行 DEV-005 业务基线

- **Status:** complete
- 用户于 2026-07-25 明确批准“DEV-005 业务基线”。
- 新增经批准的需求、验收、决定和 `ATC-REC-003@2.0.0` 组合任务卡。
- `validate` 通过（83 个规格版本），`source-status` 为 CURRENT，`impact` 仅包含本次 9 个新增 Major 规格。
- `python -m tools.specgen ready --story ATC-REC-003@2.0.0` 返回 READY。

### Phase 3: 实现垂直切片

- **Status:** in_progress
- 已开始核对 Receiving 模块、公共契约、Web 工作台和现有测试模式。
- 用户于 2026-07-25 明确要求开始 DEV-005 实现。
- 重新执行开工门禁：`validate` 通过（82 个规格版本），`source-status` 为 CURRENT，`impact` 无变更或漂移，`ATC-REC-003@2.0.0` 为 READY。
- 已新增身份评估公共契约、追加迁移、领域规则、事务仓储、服务、HTTP 端点和失败关闭资格端口的首版实现。
- 首次直接运行 `dotnet build` 被环境阻断：系统只有 SDK 9.0.305，仓库锁定 10.0.302；未降低 `global.json`，改为定位仓库锁定工具链。
- 已定位 `%LOCALAPPDATA%/OpenLIMS/dotnet` 的 SDK 10.0.302；Receiving 模块首轮 `--no-restore -warnaserror` 构建通过，0 警告、0 错误。
- 全解决方案 Release `-warnaserror` 构建通过；首轮契约测试 17/18，通过的用例已覆盖三个运行时 API，唯一失败是 OpenAPI 文档尚未列出新路径。
- 主机 OpenAPI 为显式清单；补齐三个身份评估操作后，全解决方案 Release 构建再次以 0 警告/0 错误通过。
- Receiving 单元测试 26/26、HTTP 契约测试 18/18 通过，覆盖证据完整性、结论冲突、产品类别权限、三 API、错误映射和 OpenAPI。
- 已实现 Web 三栏身份评估工作台、差异高亮、追加观察/结论、只读历史和持续隔离提示；前端首轮测试 37/38，通过项包含面板交互，唯一失败为测试 Response 被重复消费。
- 修正测试桩后，前端 lint、Vue/TypeScript 类型检查和 38/38 单元测试全部通过。
- PostgreSQL 集成测试环境检查发现：无连接变量、无本地服务、无 Docker 且 WSL 未安装；准备在临时目录启动官方免安装测试实例。
- 官方 PostgreSQL 18.4 包已下载并解压到 `C:/codex_tmp`，临时集群初始化成功；首次 `pg_ctl -o` 因 Windows 引号传递错误未启动，改用临时配置文件。
- 发现端口 55432 被昨日不响应的 DEV-004 临时进程占用，未终止该进程；DEV-005 隔离实例改用空闲的 55439 并成功启动。
- Receiving PostgreSQL 集成测试 10/10 通过，覆盖登记回归以及 DEV-005 事实追加、三动作统一阻断、并发冲突、Outbox 原子回滚和 UNKNOWN 失败关闭。
- 完成一致性复核：观察校验纳入失败审计，审计与 Outbox 共用事件 ID，Outbox 载荷加入 correlationId，新观察清除当前旧结论，Web 同步最新对象版本。
- 增加数据库级历史事实防改写触发器、外键、无敏感标识的两个指标、UNKNOWN/回滚/拒绝结构化告警和 DEV-005 运行说明。
- 收紧后全解决方案 Release 构建、前端 lint/typecheck/38 项测试仍通过；Receiving PostgreSQL 集成测试扩展为 12/12 并通过。
- 正式 Receiving task profile 通过：锁定 restore、Release warnings-as-errors 构建及 26 单元 + 18 契约 + 12 PostgreSQL 集成测试。
- Architecture profile 8/8 通过；Contracts profile 覆盖平台、Labeling、Receiving 共 52 项契约相关测试并通过。
- AGENTS 门禁的严格校验、来源状态、历史校验和生成器通过，`generate` 为 `written=0 unchanged=57 removed=0`；Python 仓库契约 39/40，唯一失败为 DEV-005 生成任务文件未加入精确期望清单。
- 补齐任务文件断言后，同一测试继续指出 Feature 总数仍为旧值 23；实际 26 对应三份新增 DEV-005 Feature，已同步精确总数和文件名断言。
- Python 仓库契约修正后 40/40 通过；第二次 `generate` 仍为 `written=0 unchanged=57 removed=0`，`specgen check` 通过。
- Web 生产构建通过；全解决方案 137/137 .NET 测试通过（含 Platform、Receiving、Labeling、架构、契约及 PostgreSQL）。
- 最终 `ATC-REC-003@2.0.0` 仍为 READY，impact 为空；48 个实际变更文件对照 26 个 allowed_paths 模式为 0 违规，`git diff --check` 通过。
- 已停止 DEV-005 自己启动的 PostgreSQL 55439 临时实例，未终止或改动昨日遗留的 DEV-004 进程。
- DEV-005 实现与验证完成；工作区保持未提交、未推送，等待用户明确授权提交和发布。

### Phase 5: 提交与发布

- **Status:** in_progress
- 用户于 2026-07-25 明确授权提交、推送并创建 PR。
- 发布前重新执行门禁：`validate` 通过（82 个规格版本）、`source-status` 为 CURRENT、`impact` 为空、`ATC-REC-003@2.0.0` 为 READY。
- 工作区仍为 DEV-005 的 48 个允许范围文件，远端为 `https://github.com/garyyue2019/OpenLIMS.git`。

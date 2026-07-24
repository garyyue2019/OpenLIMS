# DEV-003 Progress Log

## Session: 2026-07-24

### Phase 1: 将人工批准转成 READY 规格

- **Status:** in_progress
- 收到用户对 DEV-003 八项业务基线的明确批准。
- 运行任务前 `validate`、`source-status` 和 `impact`，均通过且无漂移。
- 运行 `ready --story ATC-REC-001@1.0.0`，按预期返回 BLOCKED，未开始业务编码。
- 创建分支 `codex/dev-003-receipt-registration`。
- 创建隔离计划目录并将其设置为当前活动计划。
- 完成首轮依赖盘点：确认 `ATC-PLT-003@1.0.0` 与 `OD-002@1.0.0` 已批准，其余旧依赖必须逐项核对后创建新版本，不能批量篡改状态。
- 完成旧依赖正文复核：ED-001 仍是未闭合候选，OD-009 没有决定正文；其余组织、安全、审计和架构语义与用户批准方向一致，但激活与批准必须通过新版本表达。
- 复核规格 Schema：确认各类对象的新版本必填字段和精确依赖约束，准备以新文件表达决定态、启用态和 READY 状态。
- 完成新版本语义裁剪：移除未批准的宽泛依赖，只保留用户本次批准且 DEV-003 实际需要的技术、组织、收样、授权、审计和模块边界语义。
- 读取技术锁时确认 .NET SDK、NuGet 和前端包均使用精确版本；`apps/web` 下不存在独立 pnpm 锁文件，下一步定位仓库级锁文件。
- 已定位仓库级 `pnpm-lock.yaml`，并核验 .NET、NuGet、Node、pnpm、Vue、TypeScript、Vite、Ant Design Vue、PostgreSQL、Keycloak 和 MinIO 的精确版本或镜像 digest；前述锁文件定位错误已解决。
- 新建 11 个经人工批准的 Major 规格版本；未改写任何旧规格或 PRD 来源。
- `validate --strict-warnings` 通过：71 个规格版本、389 个来源条目。
- `ready --story ATC-REC-001@2.0.0` 返回 READY。
- `verify-history` 通过；首次生成写入 11 个派生文件，`check` 通过；第二次生成 `written=0`。
- Phase 1 完成，进入模块设计与契约冻结。
- 开始 Phase 2：复核 API/Worker 模块清单、迁移入口、平台上下文、Host 认证和 Web feature 接入点；确认 receiving 必须补充自己的对象级授权公共端口并保持默认拒绝。
- 复核事务与前端接入：确定业务事实、receiving.audit_pending 与 receiving.outbox 使用同一 PostgreSQL 事务；模块通过显式 Host/Worker 清单和 Web feature registry 接入。
- 完成 Phase 2 设计冻结：确定公共契约、JWT 多维授权适配器、ServiceOrder eligibility 端口、严格 JSON、PostgreSQL 幂等并发算法、receiving 私有 Schema 和 Web 最小切片。
- 进入 Phase 3 后端与持久化实现。
- 新增 `OpenLIMS.Contracts.Receiving`，冻结 RegisterReceipt 请求、结果、授权端口、claims、capability 和稳定错误码。
- 新增 `OpenLIMS.Modules.Receiving` 的领域校验、严格 JSON API、JWT 多维授权适配器、幂等服务、PostgreSQL 私有持久化、受控迁移、失败审计和 Outbox 积压监控。
- API 与 Worker 已通过编译期清单显式注册 receiving；Worker 新增受控 `--apply-module-migration receiving`，正常启动不自动迁移。
- 解决方案和 Host 项目已引用 receiving 公共契约与模块，OpenAPI 技术文档增加 `registerReceipt` 操作。
- 使用锁定 SDK `10.0.302` 完成全解决方案 restore 与 Release `-warnaserror` 构建；新增合同和模块项目均生成成功，0 警告、0 错误。
- 新增 6 项 receiving 领域单元测试与 6 项 PostgreSQL 集成场景（正常、重放、冲突、拒绝、Outbox 回滚、并发）；全解决方案再次 0 警告构建成功。
- receiving 单元测试 6/6 通过；PostgreSQL 集成测试将在启动隔离测试数据库并接入 CI 后执行。
- 新增 10 项 HTTP 契约测试，覆盖 201 响应、匿名挑战、幂等 Header、JSON 集团覆盖、五类领域错误映射和 OpenAPI；10/10 通过。
- 新增契约测试后全解决方案继续 0 警告、0 错误。
- 新增 receiving Web 客户端、capability 呈现、分层到货表单、显式 feature/route/navigation 和成功隔离结果视图；开始补齐前端回归测试。
- 定位并确认旧 registry 测试仍锁定空业务壳；将其升级为精确校验 DEV-003 唯一业务 feature，避免以后无审批业务页面悄然进入清单。
- 前端首次 lint 仅发现 18 个可机械修复的 `<input />` 自闭合警告；未降低 `--max-warnings 0` 门禁，按既有规则修正模板。
- 第二次前端门禁 lint 已通过；typecheck 仅发现测试 fetch mock 的参数元组推断错误，改为显式标准 fetch 参数类型。
- 显式 mock 参数随后被 lint 判定未使用；保留严格 lint，并在 mock 内显式消费参数后继续验证。
- 第三次前端门禁 lint/typecheck 通过，27/28 测试通过；唯一失败是测试 stub 未显示 alert description，并非生产页面缺失，已修正 stub。
- 前端第四次完整门禁通过：lint、typecheck、28/28 单测与 production build 全部成功。
- 尝试启动本地 PostgreSQL 集成环境时发现当前机器没有 `docker` 命令；未跳过或伪造结果，继续检查本地 PostgreSQL，并已在 Linux CI 配置真实 PostgreSQL service。
- 排除 PostgreSQL 集成项目后，后端架构 7、平台单元 30、平台集成 2、平台合同 17、receiving 单元 6、receiving 合同 10 项全部通过。
- Python 仓库回归首次运行 37/40 通过；3 项失败均为 DEV-002 固定数量/任务/未批准集合断言，需要按 DEV-003 已批准证据精确升级。
- Python 固定断言已升级，40/40 通过。
- allowed-path 审计发现 API 传递依赖导致两个既有平台测试 lock 文件机械变化；在未发布/未 Seal 的 DEV-003 任务卡中加入这两个精确文件，未开放平台测试源代码范围，准备重新执行规格门禁。
- 新增四项真实 claims 授权单测；首次构建被 xUnit1051 取消令牌分析器阻断，按严格规则补传测试取消令牌。
- 授权测试修正后全解决方案 0 警告构建，receiving 单元测试增至 10/10 通过。
- Phase 3 后端/持久化与 Phase 4 Web 切片实现完成；进入 Phase 5 测试与硬化，真实 PostgreSQL 集成结果等待 Linux CI。
- 当前 73 个变更文件 allowed-path 审计 0 违规；确认本机也没有可替代 Docker 的 PostgreSQL/Podman 环境，因此真实数据库结果只能由已配置的 Linux CI 给出。
- 收敛并发契约为真实可执行的并发幂等登记，移除不可达的更新冲突错误；规格重新生成后第二次 `written=0`。
- 最终本地规格门禁通过：strict validate、source-status、history、READY、check、Python 40/40。
- 最终本地工程门禁通过：locked restore、Release `-warnaserror` build；除无本地 PostgreSQL 的 receiving integration 外，后端 76 项通过；前端 lint、typecheck、28/28 单测和 production build 通过。
- `git diff --check` 通过；额外格式验证发现 5 个本任务文件的 using 排序和 1 个未改动平台测试的既存排序问题。只修正任务范围内文件，不扩大 allowed paths 修改旧测试源。
- 已提交 `f637e15` 并推送 DEV-003 分支；正在创建 PR 以触发真实 PostgreSQL CI。首次填写描述遇到一次浏览器内部错误，未重复提交。
- PR #3 创建成功；首轮规格治理和 Windows 回归通过，Linux PostgreSQL 集成 5/6 通过。
- 唯一失败定位为测试断言语义：record 内嵌 List 不能用浅层 `Assert.Equal`。改为 `Assert.Equivalent(..., strict: true)` 验证完整重放结构，生产代码不变。

## Verification Ledger

| Gate | Result |
|---|---|
| `python -m tools.specgen validate` | PASS — 60 specs / 389 source items |
| `python -m tools.specgen source-status` | PASS — SOURCE CURRENT |
| `python -m tools.specgen impact` | PASS — no impact or drift |
| `python -m tools.specgen ready --story ATC-REC-001@1.0.0` | EXPECTED BLOCK — old task card remains proposed/blocked |
| `python -m tools.specgen validate --strict-warnings` | PASS — 71 specs / 389 source items |
| `python -m tools.specgen ready --story ATC-REC-001@2.0.0` | PASS — READY |
| `python -m tools.specgen verify-history` | PASS |
| first `python -m tools.specgen generate` | PASS — written=11 |
| `python -m tools.specgen check` | PASS |
| second `python -m tools.specgen generate` | PASS — written=0 |

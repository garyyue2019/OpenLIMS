# DEV-003 Findings

## Approved Business Baseline

- 用户于 2026-07-24 明确回复“批准 DEV-003 业务基线”。
- 部署边界是一个 OrganizationGroup 的独立部署，可包含多个法人和实验室；明确排除共享 SaaS 多租户。
- 收样层级固定为 Receipt（一次物流到货）、Container（实际包装）、ReceivedItem（一个完整销售玩具或玩具套装）。
- 同一包装内多个完整销售玩具或套装逐个登记为独立 ReceivedItem；零部件、材料和颜色留给后续业务环节。
- 型号、批次、序列号、颜色、包装状态、封识或实物状态任一不同，都必须拆分 ReceivedItem。
- 新 ReceivedItem 登记后自动进入 QUARANTINED；本任务不提供解除隔离能力。
- 权限必须同时约束集团、法人、实验室、客户和委托；管理员默认没有业务收样权限；跨实验室仅允许显式授权。
- 登记、审计和事务发件箱必须在同一事务中原子提交，并支持幂等安全重试。

## Preflight

- `main` 在任务开始时与 `origin/main` 一致且工作区干净。
- `validate`：60 个规格版本和 389 个 PRD 来源条目有效。
- `source-status`：CURRENT。
- `impact`：无规格或来源漂移。
- `ATC-REC-001@1.0.0` 按预期为 BLOCKED；旧版本不能原地修改。
- 旧任务卡未批准依赖包括平台、技术栈、组织、跨机构协作、独立部署、OD-009、收样对象模型、授权、访问控制、审计和架构边界。

## Versioning Direction

- 已封存或已分配的旧版本不原地改写。
- 用户批准改变了旧 Story 的 readiness、依赖闭包和实物粒度语义，因此必须创建新的 SemVer 版本。
- 新任务卡只能依赖精确的已批准版本，并应引用 DEV-001/DEV-002 实际交付的平台能力，而不是继续依赖未交付的占位平台任务。

## Dependency Inventory

- `ATC-PLT-003@1.0.0` 已为 `approved/ready`，是 DEV-002 实际交付的编译期业务模块接入通道。
- `ATC-PLT-000@1.0.0` 仍是未交付占位任务，不能假装批准；新 DEV-003 任务卡应改为依赖实际已交付的平台基线。
- `OD-002@1.0.0` 已批准，覆盖单集团独立部署、集团多机构和禁止共享 SaaS 多租户。
- `ED-001@1.0.0`、`ORG-STRUCT-001@0.1.0`、`ORG-COLLAB-001@0.1.0`、`SEC-DEPLOY-001@1.0.0`、`OD-009@0.1.0`、`OPS-RECEIPT-001@0.1.0`、`SEC-AUTH-001@0.1.0`、`AC-SEC-001@0.1.0`、`SEC-AUD-001@1.0.0` 和 `NFR-ARCH-001@1.0.0` 尚未批准。
- 用户批准的八项基线覆盖这些对象中与 DEV-003 相关的组织、部署、实物粒度、授权、审计、幂等和模块边界语义；仍需逐个检查现有正文是否包含超出本次批准的语义。

## Dependency Content Review

- `ED-001@1.0.0` 仍把技术栈写成待评审候选并保留多个 `PENDING_VERIFICATION` 锁值，不能只改状态；应创建反映 DEV-001/DEV-002 实际落地版本的新 Major 版本。
- `OD-009@0.1.0` 是开放决策且没有 decision 正文；必须创建决定态的新版本，正文严格采用用户批准的玩具收样粒度。
- 组织、跨机构、独立部署、授权、审计和模块边界旧正文与用户批准方向一致，但从 `in_review`/`UNKNOWN` 到 `approved`/`ENABLED` 属于激活与实施语义变化，应创建新版本而不是原地提状态。
- `SEC-AUD-001@1.0.0` 的成功事务原子性、失败尝试独立追加和敏感正文排除均属于 DEV-003 必需语义；新版本可以保持这些不变量并记录本次人工批准证据。
- `ATC-REC-001` 新版本将删除未交付占位依赖 `ATC-PLT-000@1.0.0`，改为精确依赖已交付 `ATC-PLT-003@1.0.0` 以及经批准的新技术和业务规格版本。

## Schema Constraints

- 所有依赖必须使用 `ID@x.y.z` 精确版本。
- requirement、rule 和 nfr 新版本必须显式包含 `priority` 与 `activation`；本次批准的适用项应使用 `applicability=ENABLED`。
- decision 新版本必须使用 `decision_state=decided`，不能保留 open 后再把 status 标成 approved。
- acceptance 新版本必须有完整 Given/When/Then scenario。
- Story 新版本必须保留 target release、epic、feature 和 body，并把 readiness 明确设为 ready。
- 顶层允许增加审批证据字段；为机器与人工追踪一致，新版本将统一记录用户批准原文，但不会把 AI 写成批准主体。

## New-Version Content Decisions

- 新 `SEC-AUTH-001` 不再依赖尚未批准的全局角色决策 `OD-032@0.1.0`；本版本只冻结用户已批准的 receiving.register 多维授权和默认拒绝语义。
- 新 `NFR-ARCH-001` 不再依赖未盘点的旧工程候选 `ED-002@1.0.0`，改为依赖实际批准的 DEV-002 模块接入任务与新技术栈基线。
- 新 `SEC-DEPLOY-001` 保留独立运行、数据、遥测和恢复边界，但把旧 `PENDING_REVIEW_RECORDS` 改为本次批准证据，不宣称已完成尚未执行的生产隔离演练。
- 新 `ED-001` 只锁定仓库已经存在并可由锁文件验证的开发/CI技术；生产 IdP、对象存储产品、容量、拓扑、RPO/RTO 继续排除。
- 新 `OD-009` 明确完整销售玩具/套装逐个登记；型号、批次、序列号、颜色、包装状态、封识或实物状态不同必须拆分；零部件、材料和颜色拆解留给后续环节。
- 新 `OPS-RECEIPT-001` 启用 Receipt/Container/ReceivedItem 三层模型并增加“包装数量不得自动等同实物数量”和“新实物自动隔离”不变量。

## Verified Engineering Locks

- .NET SDK `10.0.302`，`rollForward=disable`。
- ASP.NET Core / EF Core `10.0.10`，Npgsql `10.0.3`，xUnit `3.2.2`，OpenTelemetry `1.17.0`，依赖由中央包版本和各项目 lock 文件锁定。
- Node.js `24.14.1`，pnpm `10.34.5`；仓库级 `pnpm-lock.yaml` 使用 lockfile v9。
- Vue `3.5.18`、TypeScript `5.9.2`、Vite `7.3.6`、Ant Design Vue `4.2.6`、Vitest `3.2.7`。
- 开发/CI依赖使用 PostgreSQL `18.4-alpine`、Keycloak `26.4.1`、MinIO `RELEASE.2025-09-07T16-13-09Z`，均有 OCI digest；这些仍只是参考实现，不代表生产产品选择。

## Existing Module Composition

- API 与 Worker 已通过 `OpenLimsModuleCatalog` 支持编译期显式模块清单；当前生产清单为空，DEV-003 需要显式加入 receiving 模块。
- 模块可以实现 API、Worker 和 Migration 三个接口；正常启动不会自动迁移，迁移必须由显式命令调用。
- 模块描述必须使用稳定 moduleId、精确 contractVersion、独立 schemaName 和迁移程序集；重复 module/schema/route 会失败关闭。
- 平台公共上下文目前只提供部署集团、当前 actor、时钟、ID、Outbox 和审计端口；对象级法人、实验室、客户、委托和 capability 授权需要由 receiving 的版本化公共契约与适配器补足。
- API Host 已拒绝客户端通过 Header 或 Query 覆盖集团上下文，并拒绝与部署集团不一致的 JWT；receiving 仍需拒绝 JSON 正文中的未知 `organizationGroupId`。
- 现有 Host 没有业务 ServiceOrder 模块。DEV-003 必须通过公开 eligibility/authorization 端口验证委托，不能读取不存在的其他模块私表，也不能静默假设所有委托可收样。

## Persistence and UI Integration

- 平台 `ITransactionCoordinator` 使用 PostgreSQL 事务并公开只读事务访问器；receiving 可以在同一事务连接中写自己的 Schema，无需修改平台私有表或另建分布式事务。
- 平台现有 Outbox/Audit 端口的信封字段不足以承载 DEV-003 全部业务证据；receiving 将在自己的 Schema 中拥有 `audit_pending` 与 `outbox`，并与 Receipt 事实同事务写入。
- Worker 生产清单当前为空且只支持平台迁移；DEV-003 需要显式注册 receiving，并增加受控 `--apply-module-migration receiving` 入口，正常启动仍不得自动迁移。
- Web 已有编译期 feature registry。DEV-003 可新增 receiving feature、路由、导航和页面，并继续使用重复 feature/route 门禁。
- 现有架构测试仍断言生产业务模块清单为空以及 API 仅含技术路由；这两个断言必须升级为“只允许显式 receiving 模块”和“Host 不直接硬编码模块业务路由”，同时保留动态插件与跨模块私有依赖禁令。
- 现有验证脚本只接受 platform 与 module-onboarding；需要新增 receiving profile，并保证筛选实际执行 receiving 测试。

## Frozen DEV-003 Design

- 新增版本化 `OpenLIMS.Contracts.Receiving`，公开 RegisterReceipt DTO、稳定错误码和 receiving 授权/委托可收样端口；不公开数据库类型。
- 新增单一生产模块 `OpenLIMS.Modules.Receiving`，实现 API、Worker 和 Migration 三种模块契约，moduleId=`receiving`、schemaName=`receiving`、contractVersion=`1.0.0`。
- API 使用 `POST /api/v1/receipts` 和 `Idempotency-Key` Header；严格反序列化拒绝未知字段，包括正文中的 `organizationGroupId`。
- receiving 授权适配器只信任已认证 JWT 的精确 capability、法人、实验室、客户、委托和可收样委托 claims；缺失任一维度默认拒绝，`system_admin` 不自动获得业务权限。
- ServiceOrder 模块尚不存在，因此 DEV-003 通过版本化 eligibility 端口消费受信委托可收样 claim；未来 ServiceOrder 模块可替换适配器，receiving 不访问其私表。
- 幂等表以部署集团和幂等键哈希为主键，保存规范化请求哈希与首次响应；并发相同请求只创建一套对象，不同载荷稳定冲突。
- receiving Schema 拥有 receipt、container、received_item、state_history、idempotency、audit_pending、audit_attempt 和 outbox；成功事实、审计意图、Outbox 与幂等结果同事务提交。
- 授权或业务拒绝尝试通过独立连接追加到 audit_attempt；追加失败时命令失败关闭，不伪报已审计。
- Web 从 OIDC 用户 profile 只用于呈现 capability 提示；服务端始终重新授权。页面逐层编辑包装和完整玩具，成功后展示逐个 QUARANTINED 实物。

## Frontend Test Impact

- 现有 registry 回归测试明确断言“只有平台壳”，DEV-003 接入 receiving 后必须更新为平台壳加唯一 approved receiving feature。
- 新增前端测试需要证明请求不包含 `organizationGroupId`、携带 Bearer 与幂等 Header、服务端错误码稳定呈现、profile capability 只控制 UI 呈现且不替代服务端授权。

## Allowed-Path Audit Finding

- API Host 新增 receiving 项目引用后，引用 API 的既有平台合同/集成测试项目在锁定还原时必须记录 receiving 的传递依赖，因此两个既有 `packages.lock.json` 发生机械更新。
- 这些锁文件最初不在 DEV-003 allowed paths。由于任务版本尚未发布或 Seal，已在编码继续前把两个精确锁文件加入同一 READY 任务卡；没有扩大到平台测试源代码目录。
- Host 自身 lock 文件、receiving 合同/模块/测试 lock 文件均已被原有 allowed paths 覆盖。

## Concurrency Scope Clarification

- DEV-003 只创建新 Receipt 聚合，没有修改既有聚合的 API，因此不存在有意义的 expected-version 更新冲突。
- 本任务的并发边界是相同幂等键的并发登记：数据库唯一键和事务等待保证只创建一套对象，两个相同载荷请求返回同一首次结果；不同载荷返回幂等冲突。
- 任务卡的并发用例已在未发布/未 Seal 阶段收敛到这一真实操作，不保留不可达的 `CONCURRENCY_CONFLICT` 契约。

## Local Verification Environment

- 当前 Windows 环境没有 Docker、Podman、PostgreSQL 服务或 PostgreSQL 二进制，无法在本机诚实执行真实数据库测试。
- 73 个当前变更文件全部匹配 `ATC-REC-001@2.0.0` allowed paths；无越界文件。
- Linux Application CI 已配置固定 digest 的 PostgreSQL 18.4 service 和专用测试数据库，完整后端与 receiving profile 将在那里运行 6 项真实数据库测试。

## Pull Request Delivery

- 分支 `codex/dev-003-receipt-registration` 已推送，首个实现提交为 `f637e154bc3ae884626685d80c79d348a399395a`。
- GitHub 比较页确认基线为 `main`、比较分支正确、1 个提交和 73 个文件；首次填写 PR 描述时浏览器返回内部错误，尚未创建 PR。
- PR #3 已创建且无冲突。首轮 CI 中规格治理和 Windows 模块回归通过；Linux 真实 PostgreSQL 测试 5/6 通过。
- 唯一失败是测试使用 `Assert.Equal` 比较带 `List<T>` 的 record，日志显示期望和实际业务 ID、编号、版本均相同，但 List 使用引用相等；生产重放逻辑没有失败。测试改为严格深层等价比较。
- 第二轮 PR Head `686144b11df425c92fda5ce9414b5ba914251610` 的 Application CI、Windows 模块回归和 Specification governance 全部成功。
- Linux 真实 PostgreSQL 集成测试 6/6 通过，覆盖正常登记、幂等重放、幂等冲突、授权拒绝、Outbox 失败整体回滚和并发相同请求单实例化。

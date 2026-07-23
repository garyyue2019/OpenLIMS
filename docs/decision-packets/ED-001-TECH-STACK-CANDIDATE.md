# ED-001 应用技术栈与工程仓库候选方案

## 1. 文件状态

- 状态：`DRAFT / CANDIDATE / NOT APPROVED`
- 关联决策：`ED-001@0.1.0`（原始广义候选）、`ED-001@1.0.0`（工程骨架收窄草案）
- 关联边界：`ED-002@1.0.0`、`OD-002@1.0.0`；`OD-020@0.1.0`与`OD-025@0.1.0`继续作为Release业务/生产门禁
- 编制日期：2026-07-23
- 适用边界：集团多机构、每集团独立部署；当前仅有虚拟组织场景和日均 500 订单的自报输入
- 权威边界：本文件用于架构评审，不表示技术栈已经批准，也不授权业务任务卡开始编码

## 2. 已确认输入与仍未知内容

### 2.1 已确认

- 用户确认不存在必须沿用的语言、框架、数据库或云平台硬约束，允许提出推荐方案；
- 一个生产部署和数据平面只服务一个检测集团，不提供共享 SaaS 多租户；
- 集团内部需要支持多法人、多实验室、多部门和多工作中心；
- 当前容量输入是日均 500 订单；
- Release 1 方向为玩具检测；用户已选择分析化学为唯一主技术包、物理机械后移，但仍待方法/QC/仪器证据和责任角色正式批准；
- 广东华瑾及测试中心当前按虚拟组织场景处理，不是已核验的生产主体。

### 2.2 尚未确认

- 实施团队的 .NET、Vue、PostgreSQL 和 Linux 运维能力；
- 本地机房、私有云或单集团独享托管的真实生产形态；
- 企业身份源、MFA、目录同步和账号停用流程；
- 对象存储、WORM、KMS、备份和长期保留产品；
- 日均 500 订单对应的峰值小时、并发用户、样品/项目/任务倍率和附件增长；
- 可用性、RPO、RTO 的责任人、排除项和演练环境；
- 条码打印机、扫码设备、浏览器和实验室网络约束；
- 审计账本需要“事务内立即中央可见”还是接受“审计意图原子提交、中央账本最终汇聚”；
- PostgreSQL 中文检索能否满足真实订单、样品和报告搜索场景。

任何未知项都不能由 AI 用默认值标成已批准。

## 3. 推荐候选结论

| 层 | 推荐候选 | Release 1 取舍 |
|---|---|---|
| 后端 | .NET 10 LTS、ASP.NET Core 10 | 推荐 |
| 数据访问 | EF Core 10、Npgsql 10；复杂只读模型按需使用 Dapper | 推荐，Dapper 不得绕过模块边界 |
| 数据库 | PostgreSQL 18.x，固定受支持补丁版本 | 推荐 |
| 前端 | Vue 3.5、TypeScript 5.9、Vite 7、Ant Design Vue 4、Pinia 3、Vue Router 4 | 推荐 |
| Node 工具链 | Node.js 24 LTS、pnpm 10 | 推荐 |
| API | REST、OpenAPI 3.1、JSON Schema 2020-12、RFC 9457 Problem Details | 推荐 |
| 身份 | OIDC/OAuth 2.1；Keycloak 26.x 为参考 IdP，可替换为批准的企业身份源 | 候选，不自行实现密码/MFA |
| 对象存储 | S3 兼容公共端口；开发 MinIO；生产使用单集团独享合格实现 | 推荐端口，生产产品待定 |
| 异步 | PostgreSQL 事务发件箱、Inbox 幂等表、后台 Worker | 推荐 |
| 缓存 | 不引入分布式缓存；只允许非权威、短 TTL、版本化进程内缓存 | R1 默认 |
| 搜索 | PostgreSQL 索引、`pg_trgm`、有限全文检索 | R1 默认 |
| 部署 | Linux OCI 容器；开发 Docker Compose；生产专用 Linux VM、私有云或单集团独享托管 | 推荐 |
| 可观测性 | OpenTelemetry、Prometheus、Grafana、Loki；有证据时增加 Tempo；每集团独立遥测数据平面 | 推荐 |
| 后端测试 | xUnit 3、Testcontainers、ArchUnitNET、k6 | 推荐 |
| 前端测试 | Vitest、Testing Library、Playwright、axe | 推荐 |
| CI/供应链 | GitHub Actions、依赖锁定、SAST/SCA、镜像扫描、SBOM、不可变构建 | 推荐 |

候选方案不默认引入 Kubernetes、Kafka/RabbitMQ、Redis、OpenSearch、向量数据库或独立 Python AI 微服务。

## 4. 为什么选择模块化单体

OpenLIMS 的复杂性主要来自状态、规则、权限、版本、谱系、QC、报告和审计的一致性，不是互联网级横向流量。日均 500 订单尚无证据要求微服务和消息集群。模块化单体可以：

- 保持单次业务操作、审计意图和发件箱消息的事务一致性；
- 让领域边界通过代码引用、数据库 Schema、公共端口和架构测试得到约束；
- 降低每集团独立部署的安装、备份、升级和故障定位成本；
- 在有真实拆分证据时，以已有公共契约为边界拆出进程，而不是先承担分布式事务复杂度。

“模块化单体”不等于一个无边界大项目。跨模块私表访问、共享领域实体和随意调用基础设施实现都必须在 CI 中失败。

## 5. 候选仓库布局

```text
OpenLIMS.slnx
global.json
Directory.Build.props
Directory.Packages.props

src/
  host/
    api/
    worker/
  building-blocks/
  modules/
    organization/
    access/
    party/
    knowledge/
    test-scope/
    receiving/
    labeling/
    exception/
    sample-management/
    lab-execution/
    quality-results/
    reporting/
    evidence/
    audit/
    integration/
  packs/
    industry/
      toys/
    technical/
      physical-mechanical/
      analytical-chemistry/

contracts/
apps/
  web/
tests/
  architecture/
  unit/
  integration/
  contract/
  e2e/
  performance/
deploy/
scripts/
```

当前仓库尚未实施上述应用骨架。结构化任务 [`ATC-PLT-000@0.1.0`与`@1.0.0`](ATC-PLT-000-ENGINEERING-SKELETON-REVIEW.md) 均已创建但仍为 `proposed/blocked`；`ED-001@1.0.0`的15项精确锁值全部为`PENDING_VERIFICATION`，未经受控评审不得实施。现有 `ATC-REC-001` 的 `allowed_paths` 不允许顺手创建解决方案根、Host、CI 和部署目录。

## 6. 模块边界硬规则

1. 每个模块分别拥有 Domain、Application、Infrastructure、Contracts、数据库 Schema、`DbContext` 和迁移历史。
2. 模块不得引用其他模块的 Infrastructure、EF 实体、`DbContext` 或私有表。
3. 跨模块同步协作只能调用版本化公共端口；异步协作只能发布版本化事件。
4. 一个模块的写模型只能由该模块修改；不允许跨模块共享业务事务和直接更新表。
5. Shared Kernel 只保存 ID、时间、结果类型、幂等键等技术原语，不保存领域实体、权限规则或 EF Model。
6. 行业包和技术包只能依赖平台公共契约；平台内核不得反向依赖玩具行业包。
7. Pack 使用编译期注册，启用版本由 requirements lock 固定，不使用运行时动态插件或 `latest` 解析。
8. 跨模块读模型若使用 Dapper，SQL 也只能访问公开读端口或经批准的只读投影，不能访问其他模块私表。
9. 每个模块在 PostgreSQL 中使用独立 Schema；生产候选应进一步使用数据库角色限制跨 Schema 权限。
10. CI 同时运行代码依赖测试和数据库权限测试，二者不能互相替代。

## 7. 集团多机构与部署隔离

- `OrganizationGroupId` 由受保护部署配置和受信身份上下文建立，客户端 DTO 不得允许选择或覆盖集团；
- 法人、实验室、部门、工作中心和客户授权是集团内部业务维度，必须通过服务端授权和对象范围过滤控制；
- 不同集团不得共享数据库、对象存储 Bucket、凭据、密钥、IdP 运行实例、缓存、搜索索引或备份凭据；
- 同一构建镜像可以被多个集团部署复用，但运行环境、数据平面、密钥和恢复责任不能复用；
- 送检客户是 Party/Customer/ClientProgram，不是租户或部署边界；
- 未来任务卡中的旧占位名 `TENANT_ISOLATION_TEST_REQUIRED` 应在新版本中拆成集团部署边界测试和集团内组织范围测试，不能只机械改名。

## 8. 数据与对象存储

### 8.1 PostgreSQL

- 保存委托、范围、样品、任务、批次、结果、报告元数据、权限和审计事实；
- 使用数据库事务、唯一约束、乐观并发版本和幂等键保护状态转换；
- 生产迁移由独立一次性受控步骤执行，应用启动时不得自动迁移；
- 搜索索引和缓存均为可重建派生物，不能成为权限、规则或业务事实源；
- 备份候选为 pgBackRest 加 WAL 归档，但 RPO/RTO 必须由真实恢复演练证明。

### 8.2 对象存储

- 保存图片、视频、PDF、报告文件、仪器原始文件、谱图和大附件；
- PostgreSQL 只保存对象键、SHA-256、大小、类型、版本、保留状态、密钥标识和业务引用；
- 上传完成必须校验大小、哈希、媒体类型和业务授权；
- 下载必须先通过对象级授权，再签发短时 URL；禁止公开永久 URL；
- 保留、法律冻结、删除、WORM、复制和备份策略必须由质量、安全和运维共同批准。

## 9. 身份、权限和安全

- 应用只实现 OIDC/OAuth 2.1 客户端、令牌验证、会话和业务授权，不实现密码、MFA 或企业账号生命周期；
- Keycloak 只是默认参考 IdP，生产可替换为客户已有企业身份源；
- 业务授权采用角色 + 对象 + 客户 + 法人 + 实验室 + 工作中心 + 有效期 + 职责分离；
- 列表、搜索、导出、对象链接、直接 ID、后台任务和 AI 查询必须使用同一服务端授权边界；
- 机密、数据库密码、对象存储密钥和签名密钥必须由外部 Secret/KMS 管理，不写入仓库或应用配置文件；
- 审计账本与运行日志分离，运行日志不得承担受控审计证据职责。

## 10. API、错误和并发契约

- API 使用版本化 REST 和 OpenAPI 3.1；Schema 采用 JSON Schema 2020-12；
- 失败响应使用 RFC 9457 Problem Details，并增加稳定 `errorCode`、`correlationId` 和安全下一步；
- 创建和不可重复命令使用幂等键；相同键、相同请求返回同一业务结果，相同键、不同请求必须冲突；
- 更新命令使用业务版本或 ETag 做乐观并发，不能后写覆盖先写；
- API DTO 不暴露 EF 实体，不接受客户端选择集团上下文，不使用自由文本状态名驱动领域状态机；
- OpenAPI、错误码和事件 Schema 必须版本化并进入契约测试。

## 11. 异步、Outbox 与审计

候选事务路径：

```text
业务事务
  ├─ 业务数据
  ├─ module-local audit_pending
  └─ module-local outbox
         ↓ 幂等消费
中央追加式 audit.event
```

要求：

- 业务数据、审计意图和待发布事件在同一 PostgreSQL 事务提交；任一写入失败则业务事务回滚；
- Worker 使用 Inbox/消费记录保证重复、乱序和进程崩溃恢复时不重复生效；
- 中央审计账本不可用时允许重试，但不能丢失审计意图；
- 审计意图的原始业务对象、主体、动作、规则、前后版本和关联 ID 必须完整；
- 当前候选语义是“审计意图原子提交、中央账本最终一致”，需要质量负责人批准；
- 如果业务要求中央审计事件在事务内立即可见，应改用受控数据库追加端口或存储过程，不能让模块直接写审计私表。

## 12. 前端工程原则

- Vue 页面按业务 Feature 组织，不按数据库实体生成 CRUD；
- 表单和表格必须同时覆盖正常、空、加载、失败、只读、无权限、并发冲突和恢复状态；
- 权限隐藏按钮只改善体验，服务端始终重新授权；
- 扫码后显示人类可读对象、预期上下文、当前状态和安全下一步，不只依赖声音或颜色；
- 大批量操作必须显示逐项结果和失败原因，不允许全成或全败的模糊提示；
- 可访问性至少通过键盘操作、焦点、对比度、错误关联和 axe 自动检查。

## 13. 环境划分

| 环境 | 候选设计 |
|---|---|
| 本地开发 | Docker Compose 启动 PostgreSQL、Keycloak、MinIO；使用纯合成或获批准的脱敏种子 |
| CI | Testcontainers 启动真实依赖；确定性数据；不依赖开发机 Compose |
| 验证环境 | 与生产相同 OCI 镜像；独立数据平面；代表性体量；TLS、备份恢复、IdP 和仪器文件联调 |
| 生产 | 每集团独享运行和数据平面；外部 Secret/KMS；关闭调试；完整告警、备份和恢复演练 |

虚拟华瑾场景只能进入本地、CI 和工程验证；在未证明是真实脱敏资料前，不得作为生产 UAT 或上线批准证据。

## 14. 可观测性

- OpenTelemetry 统一产生 trace、metric 和结构化日志关联信息；
- Prometheus 保存运行指标，Grafana 展示技术和业务 SLI，Loki 保存受控运行日志；
- Tempo 只在端到端追踪的保存和查询需求有证据时启用；
- 每个集团使用独立的遥测接收端、存储、查询入口、访问凭据和告警通道；当前候选不批准共享日志、指标或 Trace 数据平面，也不允许跨集团查询；
- 每次请求、后台作业、事件、仪器导入和对象存储操作使用关联 ID；
- 业务异常进入可分派队列，不只写日志；
- 日志不得包含密码、令牌、未脱敏申请资料、完整原始文件或不必要个人信息；
- 审计、运行日志、业务指标和验收证据分别定义保留、访问和完整性策略。

## 15. 测试分层

| 测试层 | 工具候选 | 主要证明内容 |
|---|---|---|
| 领域单元 | xUnit 3 | 状态机、不变量、计算、资格和错误码 |
| 架构 | ArchUnitNET + 自定义数据库权限测试 | 模块引用、私表、Schema、公共端口和循环依赖 |
| 集成 | Testcontainers | 真实 PostgreSQL、IdP、S3 端口、事务、迁移和权限 |
| 契约 | OpenAPI/JSON Schema/事件 Schema 测试 | 客户端、公共端口、错误和兼容性 |
| 前端单元 | Vitest、Testing Library | 页面状态、权限态、校验和可访问性 |
| 端到端 | Playwright | 真实浏览器、收样、扫码、失败与恢复流程 |
| 性能 | k6 | 日均、P95 日、峰值小时、并发和大附件场景 |
| 恢复 | 脚本化演练 | 数据库、对象、密钥、索引、Outbox 和审计恢复 |

正向、反向、边界、权限、并发、幂等、重复、乱序、超时、恢复和审计场景必须随实现同时提交。

## 16. CI 与供应链门禁

推荐 CI 顺序：

```text
spec validate/source-status/history/check
→ dependency restore --locked
→ format/lint/typecheck
→ build --warnings-as-errors
→ unit/architecture/contract tests
→ integration/e2e tests
→ SAST/SCA/secret scan
→ SBOM/image scan
→ immutable artifact and provenance
```

要求：

- `.NET` 版本、集中包版本和锁文件由 `global.json`、`Directory.Packages.props`、`packages.lock.json` 固定；
- Node 和前端依赖由 `package.json` engines 与 `pnpm-lock.yaml` 固定；
- 容器基础镜像和部署镜像使用不可变 digest；
- 依赖机器人只能提 PR，不能自动合并数据库、身份、对象存储或框架升级；
- 生产构建不得从开发机复制；同一提交和锁文件应产生可追踪制品；
- 不得为了通过 CI 静默降低警告、跳过测试、关闭规则或删除失败证据。

## 17. 何时才引入更重组件

| 组件 | R1 默认 | 重新评审触发证据 |
|---|---|---|
| Kubernetes | 不引入 | 客户已有成熟平台、专职运维和明确高可用/弹性收益 |
| Kafka/RabbitMQ | 不引入 | 多个独立消费者、长期消息保留/重放或 PostgreSQL Outbox 无法满足批准吞吐/RTO |
| Redis | 不引入 | 多节点共享缓存有可重复性能收益，且不会承载权威规则、授权或门禁 |
| OpenSearch | 不引入 | PostgreSQL 无法满足经批准的中文检索质量、数据量或延迟 |
| 向量数据库 | 不引入 | 有版本化 AI 场景、固定评估集、隔离设计和不可替代收益 |
| Python AI 服务 | 不引入 | `IAiGateway` 后出现独立部署、模型运行或依赖隔离的真实必要性 |

每次引入都必须新建 ADR，说明故障模式、升级、备份、监控、容量、成本、退出和回滚。

## 18. 候选验证命令

以下命令已作为 `ATC-PLT-000@0.1.0` 与 `@1.0.0` 的明确验收入口写入规格；在工程骨架实际实施前它们尚不可执行，实施完成后必须由锁定工具链和 CI 逐项验证：

```powershell
dotnet restore OpenLIMS.slnx --locked-mode
dotnet build OpenLIMS.slnx -c Release --no-restore /warnaserror
dotnet test OpenLIMS.slnx -c Release --no-build
pnpm -C apps/web install --frozen-lockfile
pnpm -C apps/web lint
pnpm -C apps/web typecheck
pnpm -C apps/web test:unit
pnpm -C apps/web build
dotnet test tests/architecture/OpenLims.Architecture.Tests/OpenLims.Architecture.Tests.csproj -c Release
```

工程骨架卡最终应在 Windows 和 Linux 上分别证明 `task/architecture/contracts/all` 四个稳定入口；后续业务卡只调用其任务范围需要的入口：

```powershell
pwsh -NoProfile -File scripts/verify.ps1 -Profile task -Module platform
pwsh -NoProfile -File scripts/verify.ps1 -Profile architecture
pwsh -NoProfile -File scripts/verify.ps1 -Profile contracts
pwsh -NoProfile -File scripts/verify.ps1 -Profile all

# 后续收样业务卡示例
pwsh -NoProfile -File scripts/verify.ps1 -Profile task -Module receiving
```

脚本只做可见命令编排，不能捕获失败后返回成功，也不能根据环境静默跳过门禁。

## 19. 工程骨架任务的 Ready 条件

当前ADR同时记录了工程候选和未来生产问题。最新复核建议见[ATC-PLT-000联合评审与依赖裁剪审批包](ATC-PLT-000-JOINT-APPROVAL-PACKET.md)：工程骨架的新版本只等待技术栈、仓库、开发/CI依赖、通用模块边界和非生产恢复测试；生产IdP/S3产品、真实容量、SLA、RPO/RTO及业务Pack继续由`OD-020/OD-025`和上线门禁管理，不得混作骨架Ready条件。

独立工程骨架任务至少需要：

- 明确允许创建和修改 `OpenLIMS.slnx`、`src/`、`apps/`、`contracts/`、`tests/`、`deploy/`、`scripts/` 和应用 CI 文件；
- ED-001 由架构、工程、安全、运维负责人批准；
- 模块与数据库 Schema 所有权矩阵批准；
- 开发、CI、验证和生产环境边界批准；
- 身份、对象存储和 Secret/KMS 的端口及本地替代实现批准；
- 真实锁文件、构建、测试、迁移和镜像命令定义；
- 组织集团上下文不可由客户端选择的契约测试；
- 至少一个示范模块证明私表隔离、Outbox、审计、幂等和并发语义；
- 回滚策略不删除审计、迁移或验收证据。

## 20. 批准清单

| 决策项 | 必须批准的角色 | 当前状态 |
|---|---|---|
| .NET/Vue/PostgreSQL 主栈 | 架构、工程 | 待批准 |
| Linux OCI 与生产部署形态 | 架构、运维、安全 | 待批准 |
| 身份源和 MFA 责任 | 安全、运维、业务 | 待批准 |
| S3 实现、WORM、保留和备份 | 质量、安全、运维 | 待批准 |
| 模块/Schema 所有权矩阵 | 架构、工程 | 待批准 |
| Outbox 与中央审计一致性语义 | 质量、架构 | 待批准 |
| 容量、可用性、RPO、RTO | 业务、质量、运维 | 待批准 |
| 依赖锁定、升级和供应链门禁 | 工程、安全、运维 | 待批准 |
| 工程骨架任务与 allowed_paths | 产品、架构、工程 | 两个规格版本已创建（`ATC-PLT-000@0.1.0`与`@1.0.0`），均`proposed/blocked`，待批准 |

## 21. ED-001 完成定义

- 以上候选经责任角色评审，选项、反对意见和替代方案有记录；
- `ED-001` 使用新的 SemVer 版本形成 `approved/decided`，不得把本草案原地伪装为批准；
- 工程骨架任务获批准并创建可运行的锁定版本项目；
- 本地、CI 和验证环境能用同一代码和制品完成构建及测试；
- 架构测试证明无跨模块私表访问、循环依赖或客户端集团选择；
- 恢复演练、依赖供应链、对象存储和身份集成有可复核证据；
- 六张收样任务卡中的技术栈占位命令被真实可执行入口替换；
- 相同输入二次运行规格生成器仍为 `written=0`，应用骨架和人工测试不被需求生成器覆盖。

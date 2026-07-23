<!-- GENERATED FILE — DO NOT EDIT.
Generator: openlims-specgen@0.1.0
Sources: ATC-PLT-000@0.1.0
Edit files under spec/ and run `python -m tools.specgen generate`.
-->

# ATC-PLT-000：建立可验证的模块化单体工程骨架

## 元数据

| 字段 | 值 |
|---|---|
| 规格版本 | `0.1.0` |
| 评审状态 | `proposed` |
| 目标发布 | `REL-R1-RECEIVING-PILOT@0.1.0` |
| Epic | `EP-PLATFORM` |
| Feature | `FEAT-PLT-ENGINEERING-SKELETON` |
| 开发就绪度 | `blocked` |
| 变更级别 | `major` |
| 负责人角色 | 架构负责人, 工程负责人, 安全负责人, 运维负责人, QA负责人 |
| 影响模块 | engineering-skeleton, repository, api-host, worker-host, web-shell, module-boundaries, postgresql, identity, object-storage, outbox, audit, observability, ci, deployment, automated-test |
| 来源 | PRD-MAIN#OD-002, PRD-MAIN#OD-020, PRD-MAIN#OD-025, PRD-MAIN#SEC-DEPLOY-001, PRD-MAIN#SEC-AUD-001, PRD-MAIN#NFR-ARCH-001, PRD-MAIN#NFR-ARCH-002, PRD-MAIN#AC-DEPLOY-001 |
| 固定依赖 | ED-001@0.1.0, OD-002@1.0.0, OD-020@0.1.0, OD-025@0.1.0, SEC-DEPLOY-001@0.1.0, SEC-AUD-001@0.1.0, NFR-ARCH-001@0.1.0, NFR-ARCH-002@0.1.0, AC-DEPLOY-001@0.1.0 |
| 规格指纹 | `af9924a0b1fa776af092ce6672871ec349b8861c56407d8d104a69e72f710533` |

## 业务结果

后续AI开发任务可以在同一套已锁版本、可启动、可测试且边界可证明的工程底座上工作，不再自行选择技术栈、创建平行Host或跨模块访问私表，并禁止重新引入共享SaaS多租户数据平面。

## 主要参与者

经批准执行工程骨架任务的工程代理，以及负责评审的架构、安全、运维和QA人员

## 触发条件

工程负责人批准从当前仅含规格治理工具的仓库创建应用工程骨架，并将本任务卡交给实现代理

## 前置条件

- ED-001已形成approved/decided版本，并固定.NET、Node、PostgreSQL、前端、身份、对象存储、测试和部署版本
- OD-025已批准模块、数据库Schema、行业包和技术包所有权边界
- OD-020已批准至少用于工程验证的容量包络、依赖健康和恢复责任
- SEC-DEPLOY-001、SEC-AUD-001、NFR-ARCH-001、NFR-ARCH-002和AC-DEPLOY-001已完成适用性与语义评审
- 任务实现只允许修改allowed_paths；spec、generated/spec、业务模块、行业/技术包和真实证据均不在范围内
- 本地或CI具备批准版本的.NET SDK、Node/pnpm、Docker/OCI和PowerShell或等效Shell

## 正常路径

- 创建OpenLIMS.slnx、global.json、集中包版本、确定性锁文件和统一构建规则，所有具体版本与ED-001批准值一致
- 创建最小ASP.NET Core API Host、后台Worker Host和Vue Web Shell；只提供健康、认证接入和空业务壳，不创建检测业务接口
- 建立公共技术原语和端口：时钟、ID、关联ID、幂等键、受信Actor/Organization上下文、事务边界、模块事件、Outbox/Inbox、审计意图、对象存储和错误契约
- 建立模块项目与数据库Schema的命名/引用约束；用仅存在于tests目录的两个夹具模块证明禁止跨Infrastructure、DbContext、EF实体和私表访问
- 部署从受保护配置绑定唯一OrganizationGroup；客户端请求、Header、Query和前端状态均不能选择或覆盖集团上下文
- 提供开发用PostgreSQL、Keycloak和MinIO的Docker Compose；只使用合成配置和种子，不包含生产密钥或真实客户数据
- 建立RFC 9457 Problem Details、OpenAPI 3.1、结构化日志、OpenTelemetry、健康检查、关联ID和安全脱敏的Host级契约
- 用测试夹具证明模块内业务事实、audit_pending和outbox同事务提交；Inbox消费在重复、崩溃和重启后保持幂等
- 提供Windows PowerShell和Unix Shell稳定验证入口，分别支持task、architecture、contracts和all配置，不根据环境静默跳过门禁
- 新增应用CI，执行锁定恢复、警告即错误构建、单元/架构/契约/集成/前端测试、SAST/SCA、Secret扫描、SBOM和镜像检查
- 编写工程运行、配置、迁移、测试、依赖升级、故障诊断和回滚说明，并证明specgen重复生成不覆盖应用文件

## 失败路径

- 批准的SDK、包版本、锁文件或容器digest不一致时恢复/构建失败，不自动升级或回退到latest
- OrganizationGroup、数据库、OIDC、对象存储或Secret引用缺失时Host失败关闭，不使用共享默认值或开发凭据进入非开发环境
- 客户端提交organizationGroupId或等价集团选择字段时按未知/禁止字段拒绝，不切换部署上下文
- PostgreSQL、IdP或对象存储不可用时readiness失败并输出稳定诊断；liveness不伪报业务就绪
- 发现跨模块Infrastructure引用、共享DbContext、私表SQL或循环依赖时架构测试失败
- 测试夹具中的审计意图或Outbox持久化失败时事务整体回滚，不保留半完成业务事实
- Worker在提交业务副作用后、写入Inbox前崩溃时，恢复测试必须证明重复消息不产生重复可见结果
- 应用启动检测到待执行迁移时不得静默自动迁移生产数据库；必须由独立受控迁移步骤处理
- 日志、配置、制品或SBOM扫描发现密码、令牌、连接串、私钥或未脱敏正文时CI失败
- 任一验证脚本捕获失败却返回成功、按机器环境跳过测试或修改门禁时视为任务失败

## 领域不变量

- 一个运行部署及数据平面只绑定一个OrganizationGroup，不实现共享SaaS多租户数据库、Bucket、IdP实例、密钥、缓存、索引、日志/指标/Trace存储或备份
- 客户端永远不能选择OrganizationGroup；集团上下文只能来自受保护部署配置和受信身份
- 生产Host不得引用tests夹具；测试夹具不得演变为示例业务模块
- 平台Host和building-blocks不得拥有收样、分析化学、报告或其他领域状态机
- 模块只能依赖公共Contracts；不得引用其他模块Infrastructure、EF实体、DbContext或私有Schema
- 业务模块未来分别拥有Schema、DbContext、迁移历史、module-local audit_pending和outbox；骨架不得创建共享业务DbContext
- 行业包和技术包按requirements lock编译期注册，不使用运行时动态插件或latest解析
- spec和generated/spec不由应用脚手架修改；已发布迁移、Seal和验收证据只能追加
- 缓存、搜索、日志和指标不是授权、规则、审计或业务事实源
- 所有依赖、生成输出、测试数据和构建结果必须确定且可重复

## 数据契约

```json
{
  "artifactLocks": [
    "global.json",
    "Directory.Packages.props",
    "packages.lock.json",
    "pnpm-lock.yaml",
    "OCI image digests"
  ],
  "clientForbiddenFields": [
    "organizationGroupId",
    "connectionString",
    "clientSecret",
    "objectStorageCredential",
    "signingKey"
  ],
  "deploymentConfigServerOnly": [
    "organizationGroupId",
    "environmentName",
    "postgresConnectionSecretRef",
    "oidcAuthority",
    "oidcAudience",
    "objectStorageEndpoint",
    "objectStorageBucketRef",
    "objectStorageCredentialRef",
    "otlpEndpointRef"
  ],
  "modulePersistencePolicy": "每个未来业务模块独立Schema、DbContext和迁移历史；本卡只用tests夹具验证规则，不创建共享生产业务模型",
  "publicPorts": [
    "CurrentOrganizationContext",
    "CurrentActorContext",
    "Clock",
    "IdGenerator",
    "TransactionCoordinator",
    "OutboxWriter",
    "InboxDeduplicator",
    "AuditIntentWriter",
    "ObjectStoragePort",
    "ProblemDetailsFactory"
  ],
  "technicalPrimitives": [
    "CorrelationId",
    "IdempotencyKey",
    "ActorContext",
    "OrganizationScope",
    "Clock",
    "TransactionBoundary",
    "OutboxEnvelope",
    "InboxReceipt",
    "AuditIntent",
    "ObjectReference"
  ]
}
```

## API / 命令契约

```json
{
  "businessEndpoints": "NONE",
  "correlationHeader": "X-Correlation-Id；服务端校验格式并在缺失时生成，不信任其作为授权依据",
  "errorCodes": {
    "AUTH.ORGANIZATION_GROUP_MISMATCH": "令牌中的集团声明与当前部署绑定集团不一致时返回HTTP 403；不访问任何数据平面",
    "PLT.CONFIGURATION_INVALID": "必要部署配置缺失、未知或冲突时Host启动失败；不回退到开发默认值",
    "PLT.DEPENDENCY_UNREADY": "必要依赖不可用或探测超时时readiness返回HTTP 503；不得沿用上一次成功状态",
    "PLT.GROUP_CONTEXT_OVERRIDE_FORBIDDEN": "客户端提交集团选择字段、Header或Query时返回HTTP 400；请求整体拒绝，不能静默忽略"
  },
  "liveness": "GET /health/live，返回最小进程存活状态，不泄露依赖、版本或配置详情",
  "openApi": "OpenAPI 3.1；本卡只发布Host技术端点和契约骨架，不发布任何检测业务operationId",
  "problemDetails": "RFC 9457 + stable errorCode + correlationId；生产响应不含堆栈、Secret或内部路径",
  "readiness": "GET /health/ready，验证批准的必要依赖并返回受控状态码；详细诊断只对内部授权通道可见",
  "webRoutes": [
    "/",
    "/system/status"
  ]
}
```

## 状态转换

- API/Worker Host: STOPPED -> STARTING -> READY
- 必要依赖失败: READY -> UNREADY；恢复后通过完整探测UNREADY -> READY
- 关闭信号: READY或UNREADY -> STOPPING -> STOPPED，并等待受控在途任务或按超时安全终止
- 迁移状态只由独立迁移步骤推进；应用启动不得改变数据库Schema
- 本卡不得创建或转换任何委托、样品、批次、结果或报告业务状态

## 权限与职责分离

- liveness仅暴露最小布尔状态；readiness详细信息限制在内部网络或受控运维授权
- Web Shell可以完成OIDC登录/退出和会话失效处理，但不实现角色、客户、法人或实验室业务授权策略
- 服务端从受信令牌与部署配置构造ActorContext和OrganizationScope；客户端字段不能覆盖
- 系统管理员、开发者或运维身份不因技术角色自动获得未来业务权限
- 数据库、对象存储和IdP凭据按进程与环境最小权限分离，开发凭据不得在验证或生产环境复用
- 不同集团部署使用独立运行环境、数据库、Bucket、IdP、密钥、OTLP接收端/可观测性存储及其凭据和备份凭据

## 审计要求

- 骨架只定义AuditIntent公共端口、事务约束和测试工具，不生成虚假的检测业务审计事件
- 测试夹具证明业务事实、AuditIntent和Outbox在同一模块事务中原子提交
- 审计意图至少支持actor、organizationGroup、object、action、rule/version、before/after version、correlationId和occurredAt
- 中央审计消费者不可用时保留待发送意图并告警；不得丢弃、覆盖或把运行日志冒充审计账本
- 启动、依赖健康和配置错误进入运维日志/指标；只有经批准的受控动作才进入审计契约
- Secret、令牌、完整连接串和未脱敏业务正文不得进入日志、追踪、指标或审计

## UX 状态

- 加载态：Web Shell初始化运行时配置和认证状态，禁止显示虚假业务导航
- 未登录态：执行OIDC重定向并在回调失败时显示安全错误和关联ID
- 已登录空壳态：只显示产品壳、当前环境和获准的系统状态入口，不实现业务页面
- 后端不可用态：显示可重试状态和关联ID，不泄露连接串、内部主机或堆栈
- 配置错误态：失败关闭，不使用硬编码集团、模拟用户或开发令牌继续运行
- 可访问性：键盘、焦点、错误关联和基础axe检查通过，不只使用颜色表示健康状态

## 可观测性

- OpenTelemetry统一API、Worker和Web关联信息；日志、指标和Trace使用同一correlationId
- 每个集团的日志、指标和Trace接收端、存储、查询入口、访问凭据和告警通道均独立；本卡不批准共享可观测性数据平面或跨集团查询
- 指标至少包含进程启动、请求时延/错误、readiness依赖、Outbox/Inbox测试夹具和Worker处理结果
- 指标标签禁止客户名、对象ID、原始文件名等高基数或敏感值
- 结构化日志包含environment、service、operation、organizationGroupId、actorId哈希、correlationId和稳定错误码
- Secret与个人/客户敏感信息通过结构化白名单和自动测试防止输出
- 依赖不可用、迁移待执行、Outbox积压和重复消费异常具有独立告警入口

## 测试场景

| ID | 类型 | Given | When | Then |
|---|---|---|---|---|
| TC-PLT-000-01 | positive | 批准的SDK、包源和锁文件；干净工作区 | 连续执行两次restore/build/web build | 两次均成功；锁文件和生成源码不变化；无未声明latest依赖 |
| TC-PLT-000-02 | smoke | 开发Compose依赖可用；仅使用合成配置 | 启动API、Worker和Web | liveness/readiness符合契约；Web完成OIDC壳流程；不存在检测业务端点或菜单 |
| TC-PLT-000-03 | architecture | 测试夹具模块A和B各有Contracts与Infrastructure | 夹具A引用B的Infrastructure、DbContext或EF实体 | 架构测试失败并定位违规边；删除越界引用后通过 |
| TC-PLT-000-04 | database-boundary | 两个夹具模块使用独立Schema和数据库角色 | 模块A角色直接查询或更新模块B私表 | 数据库拒绝访问；公共端口路径仍可按契约工作 |
| TC-PLT-000-05 | security | 部署绑定集团甲；客户端提交集团乙字段/Header/Query | 访问Host技术端点或未来命令绑定管道 | 请求整体以HTTP 400和PLT.GROUP_CONTEXT_OVERRIDE_FORBIDDEN拒绝，不得静默忽略；受信集团上下文保持集团甲且不执行后续处理；记录脱敏安全诊断且不泄露集团乙信息 |
| TC-PLT-000-06 | deployment-isolation | 集团甲和乙使用独立运行实例、数据库、Bucket、IdP、密钥、OTLP/可观测性存储和备份夹具；两边可以复用同一不可变构建镜像 | 分别使用甲/乙数据库、对象存储和可观测性凭据尝试访问对方数据平面；把集团乙令牌发送到集团甲Host并尝试读取直接ID、列表、对象链接和健康详情；尝试把集团甲备份恢复到集团乙环境 | 交叉数据库、Bucket、IdP、日志/指标/Trace和备份访问全部失败且无信息泄露；集团乙令牌以HTTP 403和AUTH.ORGANIZATION_GROUP_MISMATCH拒绝且不触发数据访问；跨集团备份恢复在写入前被身份/清单校验阻断；任何运行配置都不存在共享Secret、Bucket、数据库Schema、遥测数据平面或可切换集团入口 |
| TC-PLT-000-07 | transaction | 测试夹具在保存AuditIntent或Outbox时失败 | 提交夹具业务事务 | 夹具业务事实、审计意图和Outbox全部回滚；恢复后重试只产生一套记录 |
| TC-PLT-000-08 | idempotency | 同一事件重复投递；第一次处理在副作用后模拟崩溃 | Worker重启并重新消费 | 可见副作用最多一次；Inbox/重试证据完整；原失败记录保留 |
| TC-PLT-000-09 | recovery | API已经READY | 依次中断并恢复PostgreSQL、IdP元数据或对象存储 | readiness失败关闭且有稳定诊断；不使用过期允许状态；恢复后重新完整探测并READY |
| TC-PLT-000-10 | migration | 数据库存在待执行迁移；环境为验证或生产 | 启动API和Worker | 应用不改变Schema；readiness报告迁移待处理；独立迁移命令可审计执行 |
| TC-PLT-000-11 | supply-chain | 应用制品、容器和SBOM候选 | 执行锁文件、SAST/SCA、Secret和镜像扫描 | 无未锁依赖或高危未处置项；任一Secret样例使CI失败；制品可追溯到提交和锁文件 |
| TC-PLT-000-12 | cross-platform | 相同提交、锁文件和合成夹具 | 分别运行verify.ps1和verify.sh的task/architecture/contracts/all配置 | 同名Profile执行同一门禁集合；任一失败均返回非零；不按平台静默跳过测试 |
| TC-PLT-000-13 | scope-boundary | 工程骨架构建完成 | 扫描路由、模块、迁移、导航和OpenAPI | 不存在收样、分析化学、报告或计费业务实现；不存在src/modules或src/packs生产实现；只有Host技术壳和测试夹具 |
| TC-PLT-000-14 | reproducibility | 仅有仓库、批准工具链和合成配置；无本机缓存和手工数据库状态 | 按工程说明恢复、启动、测试并停止环境 | 全流程可重复完成；清理不删除审计/测试证据；specgen check保持通过 |
| TC-PLT-000-15 | concurrency | 两个Worker实例同时收到相同消息ID和幂等键；Inbox尚无完成记录 | 两个实例并发尝试领取并提交夹具副作用 | 只有一个实例取得有效处理权；可见副作用和完成记录各只有一份；失败或失去租约的实例不删除原消息、失败证据或成功记录 |
| TC-PLT-000-16 | permission | 匿名调用者、已认证但无运维权限调用者和获许运维调用者 | 分别访问liveness、readiness摘要和详细诊断入口 | liveness只返回最小状态；未授权调用者不能获得依赖名称、地址、版本或配置；获许运维调用者只获得脱敏诊断且仍看不到Secret |
| TC-PLT-000-17 | audit | 测试夹具执行成功、失败和重试动作 | 检查事务内AuditIntent、Outbox和结构化日志 | AuditIntent包含actor、organizationGroup、object、action、rule/version、before/after version、correlationId和occurredAt；失败与重试证据保留且只追加；日志不冒充审计账本且任何载体都不含Secret或未脱敏正文 |
| TC-PLT-000-18 | negative | 非法关联ID、客户端集团字段、未知配置字段或缺失必要部署配置 | 启动Host或调用技术端点 | 返回稳定Problem Details或启动失败；不采用开发默认值、不切换集团上下文且不产生业务副作用；错误信息不泄露堆栈、Secret或内部路径 |
| TC-PLT-000-19 | boundary | 刚好满足和超过关联ID长度/字符边界的请求；依赖探测刚好满足和超过批准超时边界 | 执行Host绑定与readiness探测 | 边界内输入确定性接受；越界输入使用稳定错误拒绝；探测超时使readiness失败关闭且不会沿用上一次成功状态 |

## 明确非目标

- 不实现收样、身份、隔离、分析化学、QC业务、报告、计费或任何其他检测工作流
- 不创建src/modules/**或src/packs/**生产实现；模块边界仅通过tests夹具证明
- 不决定或批准ED-001、OD-020、OD-025、SEC-AUD-001或其他开放Decision
- 不实现完整RBAC/ABAC、用户管理、客户/法人/实验室主数据或业务管理后台
- 不接入真实仪器、ERP、电子发票、生产IdP、生产对象存储或真实客户数据
- 不引入Kubernetes、Kafka/RabbitMQ、Redis、OpenSearch、向量数据库或独立Python AI服务
- 不执行生产部署、生产迁移、真实容量承诺、RPO/RTO验收或上线切换
- 不修改spec/**、generated/spec/**、PRD、已发布迁移、Seal或验收证据

## 允许修改路径

- `OpenLIMS.slnx`
- `global.json`
- `Directory.Build.props`
- `Directory.Packages.props`
- `package.json`
- `pnpm-workspace.yaml`
- `pnpm-lock.yaml`
- `.editorconfig`
- `.dockerignore`
- `.gitignore`
- `src/host/api/**`
- `src/host/worker/**`
- `src/building-blocks/**`
- `contracts/platform/**`
- `apps/web/**`
- `tests/architecture/**`
- `tests/unit/platform/**`
- `tests/integration/platform/**`
- `tests/contract/platform/**`
- `tests/e2e/smoke/**`
- `deploy/compose/**`
- `deploy/config/**`
- `scripts/verify.ps1`
- `scripts/verify.sh`
- `.github/workflows/application-ci.yml`
- `docs/engineering/**`

## 验证命令

- `python -m tools.specgen validate --strict-warnings`
- `python -m tools.specgen source-status`
- `python -m tools.specgen verify-history`
- `python -m tools.specgen generate`
- `python -m tools.specgen check`
- `python -m unittest discover -s tests -p test_*.py`
- `dotnet restore OpenLIMS.slnx --locked-mode`
- `dotnet build OpenLIMS.slnx -c Release --no-restore /warnaserror`
- `dotnet test OpenLIMS.slnx -c Release --no-build`
- `pnpm -C apps/web install --frozen-lockfile`
- `pnpm -C apps/web lint`
- `pnpm -C apps/web typecheck`
- `pnpm -C apps/web test:unit`
- `pnpm -C apps/web build`
- `docker compose -f deploy/compose/compose.yaml config`
- `pwsh -NoProfile -File scripts/verify.ps1 -Profile task -Module platform`
- `pwsh -NoProfile -File scripts/verify.ps1 -Profile architecture`
- `pwsh -NoProfile -File scripts/verify.ps1 -Profile contracts`
- `pwsh -NoProfile -File scripts/verify.ps1 -Profile all`
- `bash scripts/verify.sh --profile task --module platform`
- `bash scripts/verify.sh --profile architecture`
- `bash scripts/verify.sh --profile contracts`
- `bash scripts/verify.sh --profile all`

## 完成定义

- 批准的SDK、包、Node、容器基础镜像和工具版本全部由可审计锁文件固定
- API、Worker和Web Shell可以在合成环境启动、探测、优雅停止并从空环境重复恢复
- Host只包含技术端点；不存在业务路由、业务导航、业务迁移或src/modules/src/packs生产实现
- 架构测试和数据库权限测试证明模块契约、私有Schema、无循环依赖及测试夹具不进入生产Host
- 集团上下文不可由客户端选择；两个集团部署夹具证明运行与数据平面独立
- 测试夹具证明事务内审计意图/Outbox原子性、Inbox幂等、崩溃恢复和失败证据保留
- OpenAPI、Problem Details、关联ID、健康、日志脱敏和OpenTelemetry契约自动测试通过
- Windows和Linux稳定验证入口执行同一门禁并正确传播失败退出码
- 应用CI完成锁定构建、测试、Secret/SAST/SCA、SBOM和镜像检查，不降低现有规格治理门禁
- 开发Compose、配置模板和文档不含生产Secret、真实客户数据或共享集团数据平面默认值
- 迁移只允许独立受控执行，应用启动不会自动修改验证或生产数据库
- 工程运行、配置、测试、升级、故障诊断、回滚和证据说明完整
- python -m tools.specgen generate二次运行written=0且check通过，证明应用骨架未侵入生成器所有权

## AI 执行约束

- 不得修改本文件；它由结构化规格生成。
- 不得把待决策项自行解释为默认业务规则。
- 不得访问其他模块私有表；必须使用批准的端口或事件契约。
- 若前置决策、依赖或测试夹具缺失，应停止实现并报告阻塞，不得猜测。

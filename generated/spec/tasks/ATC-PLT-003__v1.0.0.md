<!-- GENERATED FILE — DO NOT EDIT.
Generator: openlims-specgen@0.1.0
Sources: ATC-PLT-003@1.0.0
Edit files under spec/ and run `python -m tools.specgen generate`.
-->

# ATC-PLT-003：建立业务模块接入与验证通道

## 元数据

| 字段 | 值 |
|---|---|
| 规格版本 | `1.0.0` |
| 评审状态 | `approved` |
| 目标发布 | `REL-R1-RECEIVING-PILOT@1.0.0` |
| Epic | `EP-PLATFORM` |
| Feature | `FEAT-PLT-MODULE-ONBOARDING` |
| 开发就绪度 | `ready` |
| 变更级别 | `major` |
| 负责人角色 | 架构负责人, 工程负责人, QA负责人 |
| 影响模块 | module-composition, api-host, worker-host, web-composition, architecture-tests, verification |
| 来源 | PRD-MAIN#NFR-ARCH-001, PRD-MAIN#NFR-ARCH-002, PRD-MAIN#OD-002 |
| 固定依赖 | OD-002@1.0.0 |
| 规格指纹 | `b2a7af44a3db6fa8f6c7893009b53f1a6cfc5dc4de0c644f80eadd1f7f9218fa` |

## 业务结果

后续 AI 开发任务可以在固定边界内新增业务模块并由 API、Worker、Web、迁移和验证入口显式接入，不再复制平台脚手架或临时绕过架构门禁。

## 主要参与者

负责新增或维护 OpenLIMS 业务模块的工程人员与受控 AI 开发代理

## 触发条件

首个业务模块准备进入仓库，但现有 Host、Web、解决方案和验证脚本尚无受控接入点

## 前置条件

- ATC-PLT-000 工程骨架实现已经合并到 main 且 Application CI 与规格治理 CI 通过
- 一个部署只绑定一个 OrganizationGroup，禁止客户端选择或覆盖集团上下文
- 本任务不解释收样、身份、分析化学、QC、报告或计费业务语义
- 生产应用不得在启动时自动执行模块迁移

## 正常路径

- 定义版本化且最小化的服务端模块描述与 API、Worker 组合契约
- API 和 Worker 通过编译期显式清单注册模块，不扫描或解析运行时最新版规则
- 模块声明稳定 moduleId、contractVersion、schemaName 和能力入口，重复或非法声明在启动时失败关闭
- 为前端建立显式功能清单与路由组合器，重复路由名或路径在构建或测试时失败
- 用仅存在于 tests 下的夹具模块证明 API、Worker、迁移和 Web 组合通道可用
- 把架构门禁从禁止任何业务模块改为禁止跨模块私有实现、私表、DbContext 和循环依赖
- 扩展跨平台验证入口，使平台、模块接入夹具和未来 receiving 模块可以使用稳定命令验证

## 失败路径

- 重复 moduleId、重复 schemaName、重复路由或不兼容 contractVersion 时启动或构建失败
- 业务模块引用其他模块 Infrastructure、实体、DbContext 或私有迁移时架构测试失败
- API 或 Worker 尝试在正常启动路径自动执行迁移时测试失败
- 客户端或模块尝试覆盖 OrganizationGroup 时沿用稳定平台错误并拒绝
- 模块接入失败时不得退回静默跳过、空实现或开发默认值

## 领域不变量

- Host 是显式组合根但不拥有业务状态机、业务表或业务规则
- 每个业务模块拥有独立 Schema、迁移历史和持久化边界
- 跨模块同步仅使用版本化公共端口，异步仅使用版本化事件
- 一个运行和数据平面只服务一个 OrganizationGroup，禁止引入共享 SaaS 多租户
- 模块清单由代码和锁定构建决定，运行时不能选择 latest、范围版本或动态下载插件
- 本任务不得新增任何生产业务路由、业务页面、业务数据库表或业务状态转换

## 数据契约

```json
{
  "compositionRules": [
    "stable identifiers",
    "exact contract versions",
    "unique schema names",
    "explicit registration",
    "fail closed"
  ],
  "serverModuleDescriptor": [
    "moduleId",
    "contractVersion",
    "schemaName",
    "apiRegistration",
    "workerRegistration",
    "migrationAssembly"
  ],
  "webFeatureDescriptor": [
    "featureId",
    "contractVersion",
    "routes",
    "navigationEntries"
  ]
}
```

## API / 命令契约

```json
{
  "compatibility": "本任务保持现有 /health/live、/health/ready、/system/status 和 /openapi/v1.json 行为兼容；夹具端点仅存在于测试宿主。",
  "compositionSurface": [
    "AddOpenLimsModule",
    "MapOpenLimsModuleEndpoints",
    "AddOpenLimsWorkerModule"
  ],
  "publicOperationsAdded": []
}
```

## 状态转换

- 无业务状态转换；模块组合仅发生在应用构建和启动阶段

## 权限与职责分离

- 模块只能通过平台公共端口取得部署集团和当前操作者上下文
- 模块注册本身不能授予业务 capability 或对象访问权
- 现有匿名、认证和集团令牌边界保持不变
- 未来业务端点仍必须由各任务卡声明对象级授权和失败语义

## 审计要求

- 本任务不产生业务审计事件
- 启动日志可以记录非敏感 moduleId 与 contractVersion，但不得记录凭据、连接串或客户数据
- 业务模块未来仍必须通过模块自身的审计意图和 Outbox 与业务事实同事务提交

## UX 状态

- 无模块时现有首页和系统状态页保持可用
- 测试夹具证明功能清单可以注册路由和导航项
- 重复 featureId、路由名或路径在单元测试中失败
- 本任务不新增任何生产业务导航或页面

## 可观测性

- 启动日志记录已注册 moduleId、contractVersion 和宿主类型
- 模块注册失败使用稳定错误类别且进程失败关闭
- 不得把模块、客户或实验室名称用作无界指标标签
- 现有健康检查不得因空模块清单而伪报依赖成功

## 测试场景

| ID | 类型 | Given | When | Then |
|---|---|---|---|---|
| TC-PLT-003-01 | positive | 一个 tests 下的合法夹具模块 | API 和 Worker 组合该模块 | 模块只注册一次；测试端点与后台服务可解析；生产 Host 不出现夹具路由 |
| TC-PLT-003-02 | boundary | 两个模块声明相同 moduleId 或 schemaName | 构建模块清单 | 稳定失败；不得静默覆盖先注册模块 |
| TC-PLT-003-03 | architecture | 模块项目尝试引用另一模块私有实现或 DbContext | 运行架构门禁 | 测试失败并指出非法依赖 |
| TC-PLT-003-04 | recovery | 模块存在待执行迁移 | API 或 Worker 正常启动 | 不自动修改 Schema；readiness 按依赖真实状态返回 |
| TC-PLT-003-05 | frontend | 合法前端功能清单 | 组合路由和导航 | 确定性生成结果；重复 featureId、路由名或路径被拒绝 |
| TC-PLT-003-06 | security | 测试模块尝试提交或覆盖另一 OrganizationGroup | 请求进入 Host | 沿用平台稳定错误拒绝；不切换集团上下文 |
| TC-PLT-003-07 | regression | DEV-002 完整变更 | 运行 Windows、Linux、后端、前端和规格门禁 | 现有平台测试全部通过；相同规格输入第二次生成 written=0 |

## 明确非目标

- 不实现 Receipt、Container、ReceivedItem 或任何收样业务
- 不实现对象级授权策略、角色矩阵或客户业务权限
- 不实现分析化学、物理机械、微生物、QC、仪器或报告能力
- 不引入动态插件市场、运行时模块下载或脚本执行
- 禁止引入共享数据库、共享 Bucket、共享 IdP 或任何共享 SaaS 多租户模式
- 不修改 PRD 来源文档或人工伪造生成文件

## 允许修改路径

- `spec/stories/ATC-PLT-003__v1.0.0.json`
- `generated/spec/**`
- `.planning/2026-07-24-dev-002-business-module-onboarding/**`
- `OpenLIMS.slnx`
- `Directory.Build.props`
- `Directory.Packages.props`
- `contracts/platform/**`
- `src/host/api/**`
- `src/host/worker/**`
- `src/building-blocks/**`
- `apps/web/src/**`
- `apps/web/tsconfig*.json`
- `tests/architecture/**`
- `tests/unit/platform/**`
- `tests/integration/platform/**`
- `tests/contract/platform/**`
- `tests/fixtures/modules/**`
- `tests/test_repository_contract.py`
- `scripts/verify.ps1`
- `scripts/verify.sh`
- `.github/workflows/application-ci.yml`

## 验证命令

- `python -m tools.specgen ready --story ATC-PLT-003@1.0.0`
- `pwsh -File scripts/verify.ps1 -Profile task -Module module-onboarding`
- `pwsh -File scripts/verify.ps1 -Profile architecture`
- `pwsh -File scripts/verify.ps1 -Profile contracts`
- `corepack pnpm@10.34.5 --dir apps/web lint`
- `corepack pnpm@10.34.5 --dir apps/web typecheck`
- `corepack pnpm@10.34.5 --dir apps/web test:unit`
- `python -m tools.specgen check`

## 完成定义

- 服务端与前端组合契约均有正向、反向、边界和重复注册测试
- 测试夹具证明 API、Worker、Web 和受控迁移接入点可用，但生产应用无新增业务能力
- 架构测试允许受控模块存在并阻断跨模块私有实现、DbContext、私表和循环依赖
- Windows 和 Linux 验证脚本支持 module-onboarding 稳定配置且正确传播失败退出码
- 现有平台合同、权限、恢复、审计、前端和真实依赖 Smoke 回归不退化
- 规格生成、历史验证和确定性二次生成门禁全部通过

## AI 执行约束

- 不得修改本文件；它由结构化规格生成。
- 不得把待决策项自行解释为默认业务规则。
- 不得访问其他模块私有表；必须使用批准的端口或事件契约。
- 若前置决策、依赖或测试夹具缺失，应停止实现并报告阻塞，不得猜测。

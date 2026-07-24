# DEV-002 Findings

## Initial Repository Findings
- `main` 已合并工程骨架，工作区在任务开始时干净。
- `python -m tools.specgen validate`：59 个规格版本、389 个 PRD 来源条目有效。
- `source-status` 为 CURRENT，`impact` 无新增、变化、删除或来源漂移。
- 现有架构测试明确断言 `src/modules` 与 `src/packs` 不存在，只适用于工程空壳阶段。
- 现有 `scripts/verify.ps1` 的 `-Module` 只接受 `platform`，业务任务卡无法使用真实模块验证命令。
- 现有 API Host 只映射四个技术路由，没有显式业务模块组合入口。
- 现有 Worker 没有模块后台服务组合入口。
- 前端路由在单一 `router.ts` 中静态定义，没有受控功能清单或冲突检测。
- `ATC-REC-001@1.0.0` 的 allowed paths 不允许修改解决方案、Host 或现有前端路由，因此必须先完成 DEV-002。
- `OpenLIMS.BuildingBlocks.Platform` 当前是普通 SDK 项目，只引用平台 Contracts、Npgsql 与 S3；若组合契约直接暴露 ASP.NET 类型，需要显式评估 FrameworkReference，不能偶然把 Web 依赖扩散到 Worker。
- API 与 Worker 当前都只有平台引用；Worker 的迁移入口只接受 `--apply-platform-migration`，生产启动路径没有自动迁移。
- Web 当前在单一 `router.ts` 静态声明三个技术路由，`App.vue` 也硬编码系统状态导航；DEV-002 应抽出确定性组合函数，同时保持现有三个路由和导航行为不变。

## Approved Boundary
- 只做业务模块接入通道。
- 服务端采用编译期显式注册，不做运行时插件扫描。
- 测试夹具只能位于 `tests/**`，不能成为生产业务模块。
- 不实现对象级授权策略；只保留平台上下文端口边界。
- 不新增生产业务 API、页面、表和状态机。

## Frontend Implementation Review
- 新增 `web-feature.ts`，对 featureId、精确 SemVer、路由和导航进行显式组合。
- 组合器对重复 featureId、路由名、等价路由路径以及非法版本/路径/导航目标失败关闭。
- 生产 `web-feature-registry.ts` 只注册原有 PLATFORM-SHELL；首页、系统状态和鉴权回调三条路由保持不变。
- `router.ts` 和 `App.vue` 已消费组合结果，没有新增业务导航或页面。
- 前端代理报告 lint、typecheck、24 项 Vitest 与 production build 全部通过；主代理已完成代码阅读，未发现越界修改。

## Backend and Runtime Preliminary Review
- 服务端新增稳定 `ServerModuleDescriptor`、显式 `OpenLimsModuleCatalog`、API/Worker 组合接口和重复 module/schema/route 失败关闭。
- 生产 API 与 Worker 使用编译期空模块清单，测试夹具仅位于 `tests/fixtures/modules/**`，生产程序集不引用夹具。
- `module-onboarding` 测试使用 xUnit `Profile=module-onboarding` Trait，避免验证脚本只执行零测试。
- 架构门禁已从“禁止任何模块目录”转向项目引用、跨模块私有实现、公共契约持久化类型和循环依赖检查。
- 最终整合必须重点验证：受控迁移只有显式入口、生产 Host 无夹具端点、Windows/Linux profile 均实际执行测试、架构静态规则存在对应反向测试。

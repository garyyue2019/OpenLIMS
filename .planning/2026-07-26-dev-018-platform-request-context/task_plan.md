# DEV-018 请求上下文与对象级授权（正式化 + 跨组织隔离验证）

## 目标

为已在全部模块落地的请求上下文/对象级授权行为补上正式任务卡（建议清单第 2 张，最后一张无 OD 阻断卡）：SEC-AUTH-001 的部署绑定集团上下文、对象级能力授权失败关闭、跨组织不泄露存在性（AC-SEC-001）与 correlation 贯穿，全部以真实端口跨模块 E2E 验证；纯正式化 + 测试，不改任何产品代码。

## 阶段

1. [completed] 侦察：SEC-AUTH-001/AC-SEC-001 已批准；模块行为——actor 缺失/组不匹配→NOT_AUTHORIZED、能力拒绝→NOT_AUTHORIZED+attempt 审计、跨组读取因组内 load 返回 null→OBJECT_NOT_ACCESSIBLE（不泄露存在性）、correlation 原样入 platform.audit_intent 与 audit_attempt。
2. [completed] 规格 BUS-PLT-002 + ATC-PLT-001@1.0.0 并 READY；仓库契约测试 136→138、特性 47→48。
3. [completed] 在 tests/e2e/chain 新增 RequestContextAuthorizationE2ETests：能力拒绝失败关闭、跨组织对象隔离（AC-SEC-001）、correlation 贯穿断言。
4. [in_progress] 完整门禁，CI 全绿后按授权提交/PR/合并。

## 约束

- 零产品代码变更；仅规格 + E2E 测试 + 文档。
- 不触碰未决 OD；PRD 只读。

## 错误记录

| 错误 | 尝试 | 处理 |
|---|---:|---|

# DEV-007 进度

## 2026-07-26

- 完成四项前置检查；确认旧 `ATC-REC-006@1.0.0` 为 BLOCKED。
- 从 `main` 创建分支 `codex/dev-007-controlled-release`。
- 建立持久化计划、发现和进度记录。
- 追加 `ATC-REC-006@2.0.0` 精简批准任务卡；为通过严格门禁且不改写旧规格，未保留多余 requirement/acceptance 后继版本。
- 后继任务卡已返回 READY。
- 已实现不可变 ReleaseDecision、追加迁移、单一质量授权、正常/受限状态转换、原子审计与 Outbox。
- 已新增 `ReceivingEligibilityPort@v2`，并保持 v1 失败关闭语义。
- 已接入 HTTP API、OpenAPI 和轻量 Web 放行面板。
- 已补齐领域、HTTP、Web、PostgreSQL 并发/事务/权限/恢复测试。

## 验证记录

- `python -m tools.specgen validate`：PASS。
- `python -m tools.specgen source-status`：PASS，SOURCE CURRENT。
- `python -m tools.specgen impact`：PASS，无影响项。
- `python -m tools.specgen ready --story ATC-REC-006@1.0.0`：预期 BLOCKED。
- `python -m tools.specgen ready --story ATC-REC-006@2.0.0`：READY。
- `.NET build`：PASS，0 warnings / 0 errors。
- Receiving unit tests：PASS，43/43。
- Receiving contract tests：PASS，34/34。
- Python repository tests：PASS，40/40。
- Receiving unit tests：PASS，43/43（Release）。
- Receiving contract tests：PASS，34/34（Release）。
- Architecture tests：PASS，8/8（Release）。
- Web lint/typecheck/build：PASS；Web unit：PASS，47/47。
- PostgreSQL 集成测试：本机未配置 `OPENLIMS_TEST_POSTGRES_CONNECTION`，已编译并交由仓库验证脚本/CI 执行。
- 最终本地门禁：严格规格、来源、历史、二次生成幂等、spec check、Python、Release build、Receiving 单元/契约、架构、Web lint/typecheck/unit/build 全部 PASS；待远端 CI 执行 PostgreSQL 集成测试。

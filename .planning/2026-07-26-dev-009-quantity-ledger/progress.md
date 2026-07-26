# DEV-009 进度

## 2026-07-26

- DEV-008 合并后，用户确认继续 DEV-009；经候选分析用户选择 `ATC-QTY-001 不可变数量流水与并发预留`。
- 前置门禁通过：validate（93 规格版本）、SOURCE CURRENT、impact 无影响项。
- ready 检查确认 `ATC-QTY-001@1.0.0` 尚不存在。
- 从 `main@c5af084` 创建分支 `codex/dev-009-quantity-ledger`，建立独立规划、发现和进度记录。
- 已提取 OPS-QTY-001~004、OPS-LIN-001、BP-018 和派生样品状态机语义；确认代码和规格中均无数量流水实现。
- 识别阻断决定：OD-010（计量维度、精度、损耗、容差）在 spec/decisions 中不存在，需用户批准最小口径。
- Phase 1 完成；Phase 2 等待用户批准 DEV-009 最小业务基线。
- 用户明确批准 DEV-009 业务基线（含 OD-010 最小口径：单维度单单位账户、禁止跨维度换算、BP-018 不可计量对象拒绝建账、冲销重记更正、expected-version 并发预留、quantity.post 单一能力、失败关闭可用量查询端口）；Phase 2 完成，Phase 3 开始。
- 已追加 6 项批准源规格（OD-010、BUS-QTY-001~003、AC-QTY-001、ATC-QTY-001 均 @1.0.0）；strict validate 一次通过（99 规格版本）。
- impact 确认 6 项均为经用户批准的直接 major 增量；generate 后 `ATC-QTY-001@1.0.0` 返回 READY，spec check 通过，二次 generate written=0。
- 仓库契约测试按预期需要机械更新：规格计数 93→99、任务/feature 生成物集合、@1.0.0 交付集合；更新后 Python 40/40 通过。
- Phase 3 完成，正式进入 allowed_paths 内实现。
- 已创建 Quantity 契约、领域规则、授权、迁移、持久化、服务、可用量端口、Endpoint、遥测和模块组合，并接入 API、Worker、OpenAPI、Solution 与 verify 脚本（quantity 过滤）。
- dotnet restore 生成新项目锁文件并机械更新 5 个引用 API Host 的测试项目锁图；Release/warnaserror 构建一次通过，0 警告 0 错误。
- 重建 D:\pgtest 隔离 PostgreSQL 16.4（127.0.0.1:55442）。Quantity 单元 14/14、契约 11/11、架构 10/10 一次通过。
- 集成测试首次 7/8：测试用例试图冲销带预留关联的消耗条目，违反本卡"预留关联条目不可冲销"的领域规则；已把用例改为 LOSS 的冲销加重记链（也覆盖 RESTATE 数据库路径），8/8 通过，未放宽领域规则。
- 已撰写 docs/domain/quantity/DEV-009-quantity-ledger.md；Phase 4/5 完成，进入 Phase 6 全量门禁。
- 全解决方案首次并行运行时，Quantity 与 Scope 集成测试在共享 platform.audit_intent/outbox 上因 truncate 与全局计数断言互相干扰（CI 也用同一条 `dotnet test OpenLIMS.slnx` 命令，DEV-007 的 CI 失败疑似同类问题）。
- 修复方式：Quantity 集成测试固定使用专用数据库 `openlims_quantity_test`（不存在则自动创建，容忍 42P04 并发创建），不修改 Scope/Receiving 既有测试，不放宽任何断言。
- 修复后全解决方案 16 个测试项目 242/242 全部通过；Profile=quantity 过滤（14+11+8）与 Architecture 过滤（10）门禁通过。
- Phase 6 门禁：strict validate（99 规格版本）、SOURCE CURRENT、HISTORY PASSED、READY、spec check、二次 generate written=0、Python 40/40、locked restore、Release/warnaserror 0 警告 0 错误。
- 路径审计：60 个变更文件全部位于任务卡 allowed_paths，outside_allowed=0；PRD 未修改，generated/spec 只经生成器更新。
- Phase 6 门禁全部完成；按约束等待用户的提交/推送指令。
- 用户指示提交并推送；已提交 `351c12c`（60 个文件，全部在 allowed_paths 内，无构建产物），推送到 `origin/codex/dev-009-quantity-ledger`。
- 经 GitHub API 创建 PR：https://github.com/garyyue2019/OpenLIMS/pull/9，等待远端 CI。
- 两个提交的 Specification governance 与 Application CI（含 Linux PostgreSQL 集成测试和专用数据库隔离修复）均 success。等待用户合并指令。

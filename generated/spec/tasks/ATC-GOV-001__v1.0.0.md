<!-- GENERATED FILE — DO NOT EDIT.
Generator: openlims-specgen@0.1.0
Sources: ATC-GOV-001@1.0.0
Edit files under spec/ and run `python -m tools.specgen generate`.
-->

# ATC-GOV-001：实施 DEV-019 冻结 R1 适用性基线

## 元数据

| 字段 | 值 |
|---|---|
| 规格版本 | `1.0.0` |
| 评审状态 | `approved` |
| 目标发布 | `REL-R1-RECEIVING-PILOT@1.0.0` |
| Epic | `EP-GOVERNANCE` |
| Feature | `FEAT-GOV-R1-BASELINE` |
| 开发就绪度 | `ready` |
| 变更级别 | `major` |
| 负责人角色 | 产品负责人, 技术负责人, 质量负责人, QA负责人 |
| 影响模块 | release-governance, applicability, industry-pack, technical-pack, baseline, traceability, automated-test |
| 来源 | PRD-MAIN#OD-001, PRD-MAIN#BUS-DOMAIN-001, PRD-MAIN#BUS-REQ-002, PRD-MAIN#RULE-026 |
| 固定依赖 | ED-001@2.0.0, OD-002@1.0.0, OD-001@1.0.0, BUS-GOV-001@1.0.0, NFR-ARCH-001@2.0.0 |
| 规格指纹 | `a0b1e6f5d4519b03c508bc33457107bb75e228dd2623e6d4313e7cfe2274b3c2` |

## 业务结果

R1 适用性从分散在各规格 activation 字段的隐式状态升级为单一冻结基线：任何未来卡启用新行业包、技术包或未决 OD 能力都必须先通过后继决策版本与新快照，防止试点范围静默扩张（OD-001 反范围蔓延目标）。

## 主要参与者

规格治理维护者（记录决策、冻结快照）与仓库契约测试（持续守护基线）

## 触发条件

治理维护者依据用户 OD-001 决定冻结 Release 1 适用性基线

## 前置条件

- OD-001 已获用户决定（2026-07-26）
- PRD 来源基线一致（source-status CURRENT）
- 生成锁存在且与规格一致

## 正常路径

- OD-001__v1.0.0.json 以 decided/approved 落盘：玩具×3岁+硬质塑胶非电动×中国内销×物理机械，分析化学降为延后候选，v0.1.0 原提案保留不动
- BUS-GOV-001 固化基线不变量（core=ENABLED、纺织 DISABLED、AI DISABLED、未决 OD 不入生产语义、快照冻结规则）
- specgen snapshot --name r1-applicability-baseline 生成不可覆盖锁快照，内容与当前生成锁一致
- 仓库契约测试新增基线断言：traceability 中激活状态逐组匹配、快照文件存在且含 OD-001@1.0.0 与全部 GOV 规格
- readiness 报告不再有'决策 OD-001 尚未形成 decided 结论'类阻断（GOV 卡自身 READY）

## 失败路径

- 重复 snapshot 同名被 specgen 拒绝（快照不可覆盖），基线变更只能追加新名称
- 任何 approved+core 规格出现 UNKNOWN 适用性时 validate 失败（unknown_applicability_policy=block）
- 纺织/AI 激活状态被改动或快照被删改时仓库契约测试失败
- PRD 来源漂移时 snapshot 被 require_sources_current 阻断

## 领域不变量

- OD-001 决策以新版本文件追加记录，v0.1.0 提案原样保留（决策追加式）
- 基线快照不可覆盖，变更必须新快照名并伴随后继决策版本
- 本卡不改变任何模块的激活语义——纺织与 AI 的 DISABLED 状态、未决 OD 的 open 状态原样保持
- 灯塔证据政策不放宽：SYNTHETIC_SANDBOX_ONLY 定位与生产启用退出条件由 OD-001@1.0.0 原文保留
- 零 .NET 产品代码与测试变更——本卡交付物为规格、快照与 Python 治理测试
- 不修改 REL-R1-RECEIVING-PILOT 发布基线文件，不创建 Seal

## 数据契约

```json
{
  "activationBaseline": [
    "core 已批准规格 → ENABLED",
    "BUS-TEX-001..005@1.0.0 → enabled_pack/DISABLED",
    "BUS-AI-001..003@1.0.0 → conditional/DISABLED"
  ],
  "baselineSnapshot": [
    "spec/baselines/r1-applicability-baseline.lock.json：specgen 生成锁的不可覆盖副本，含全部 139+ 规格版本哈希与 OD-001@1.0.0"
  ],
  "decision": [
    "OD-001@1.0.0：decided/approved，pilot_slice{industry_pack, product_eligibility, target_market, primary_technical_pack, deferred_technical_pack, excluded_technical_capabilities, lighthouse_evidence_state}，handoff_rule，revision_rule"
  ]
}
```

## API / 命令契约

```json
{
  "errors": [],
  "operations": [],
  "publicPort": "无——本卡不新增任何 HTTP 端点、模块或运行时能力"
}
```

## 状态转换

- OD-001：open(v0.1.0 保留) → decided(v1.0.0 追加)；基线：无快照 → r1-applicability-baseline 冻结（不可覆盖）

## 权限与职责分离

- 不新增能力或 claim；快照与决策记录属规格治理操作，由仓库门禁守护

## 审计要求

- 决策与基线演进由规格版本文件、生成锁与不可覆盖快照追溯；verify-history 保证已封存版本不被篡改

## UX 状态

- 本卡不新增前端页面
- 无客户端交互面——交付物为治理工件

## 可观测性

- 基线漂移由仓库契约测试在 CI 阻断；readiness 报告反映 OD-001 决策后各卡阻断状态变化

## 测试场景

| ID | 类型 | Given | When | Then |
|---|---|---|---|---|
| TC-GOV-001-01 | positive | 用户 2026-07-26 决定 | validate/ready 运行 | OD-001@1.0.0 approved+decided；approval_evidence 含用户原话；v0.1.0 原样保留；依赖 OD-001@1.0.0 的故事不再因决策未闭合阻断 |
| TC-GOV-001-02 | regression | traceability 生成物 | 仓库契约测试运行 | 全部 approved core 规格 ENABLED；BUS-TEX-001..005 enabled_pack/DISABLED；BUS-AI-001..003 conditional/DISABLED；无 approved 规格为 UNKNOWN |
| TC-GOV-001-03 | regression | r1-applicability-baseline.lock.json | 仓库契约测试运行 | 快照存在且为合法 JSON；含 OD-001@1.0.0 与 BUS-GOV-001@1.0.0、ATC-GOV-001@1.0.0；与当前生成锁的对应条目一致 |
| TC-GOV-001-04 | negative | 快照已存在 | 再次 snapshot 同名 | specgen 报错拒绝覆盖 |

## 明确非目标

- 不决定 OD-025（平台/包产品边界）、OD-012（权威来源）、OD-031 延后清单及报告链/AI 各未决 OD
- 不启用纺织或 AI 激活状态
- 不实施 INST/QC 卡（后续 DEV-020/021）
- 不修改发布基线文件，不创建 Seal、tag、GitHub Release 或部署
- 不新增 .NET 代码或模块

## 允许修改路径

- `spec/decisions/OD-001__v1.0.0.json`
- `spec/requirements/BUS-GOV-001__v1.0.0.json`
- `spec/stories/ATC-GOV-001__v1.0.0.json`
- `spec/baselines/r1-applicability-baseline.lock.json`
- `generated/spec/**`
- `.planning/2026-07-26-dev-019-gov-applicability-baseline/**`
- `tests/test_repository_contract.py`
- `docs/domain/governance/**`

## 验证命令

- `python -m tools.specgen ready --story ATC-GOV-001@1.0.0`
- `python -m tools.specgen validate --strict-warnings`
- `python -m tools.specgen check`
- `python -m unittest tests.test_repository_contract`

## 完成定义

- OD-001@1.0.0 decided 且忠实记录用户决定（含技术包翻转说明）
- BUS-GOV-001 不变量与激活基线一致
- 快照生成且不可覆盖行为验证
- 仓库契约测试含基线断言并通过
- 全仓验证通过且二次 generate written=0
- 所有变更位于 allowed_paths

## AI 执行约束

- 不得修改本文件；它由结构化规格生成。
- 不得把待决策项自行解释为默认业务规则。
- 不得访问其他模块私有表；必须使用批准的端口或事件契约。
- 若前置决策、依赖或测试夹具缺失，应停止实现并报告阻塞，不得猜测。

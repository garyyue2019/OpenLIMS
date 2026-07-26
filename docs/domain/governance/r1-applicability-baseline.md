# R1 适用性基线（DEV-019 / ATC-GOV-001）

## OD-001 决策（2026-07-26 用户决定）

> "定 OD-001：玩具 + 机械物理方法族"

**Release 1 唯一试点切片**：玩具婴童产品 × 3岁及以上常规硬质塑胶非电动玩具 × 中国内销 × **物理机械方法族**。

- 技术包主选翻转：2026-07-23 意向摘录中"分析化学取代物理机械推荐"始终处于 PENDING_ROLE_APPROVAL、未获正式批准；本决定恢复物理机械为主选，分析化学降为延后候选，微生物/生物保持排除。
- 决策以 `spec/decisions/OD-001__v1.0.0.json` 追加记录（decided/approved），v0.1.0 提案原样保留。
- 灯塔证据政策不放宽：SYNTHETIC_SANDBOX_ONLY 定位与生产启用退出条件（真实法人核验、付费灯塔证据、角色正式批准）由决策原文保留。

## 冻结机制

| 工件 | 作用 |
|---|---|
| `spec/decisions/OD-001__v1.0.0.json` | 试点切片唯一依据；边界变更须后继 OD 版本 + 用户重批 |
| `spec/requirements/BUS-GOV-001__v1.0.0.json` | 基线不变量（core=ENABLED、纺织/AI=DISABLED、未决 OD 不入生产语义） |
| `spec/baselines/r1-applicability-baseline.lock.json` | specgen snapshot 生成的不可覆盖锁快照（141 规格版本哈希） |
| `tests/test_repository_contract.py::test_r1_applicability_baseline_is_frozen_and_consistent` | CI 持续守护：决策内容、激活状态分组、快照与生成锁一致性 |

## 激活状态基线

- **core（已批准）→ ENABLED**：平台、收样、标识、范围、数量、分配、批次、结果、计费、GOV。
- **enabled_pack → DISABLED**：纺织行业包（BUS-TEX-001..005，契约切片已冻结待启用）。
- **conditional → DISABLED**：AI 旁路（BUS-AI-001..003，OD-006/007 未决）。
- 任何 approved 规格不得为 UNKNOWN（unknown_applicability_policy=block）。

## 解锁路径

未决 OD（OD-025/OD-011/OD-022/OD-029/OD-006/OD-007/OD-012 等）关联能力进入生产语义的唯一路径：后继 OD 版本 decided + 新基线快照（追加名，不覆盖）。

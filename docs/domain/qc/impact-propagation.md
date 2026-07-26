# QC 影响传播（DEV-021 / ATC-QC-001）

OD-001 已决（玩具 × 物理机械方法族）解锁的第三张卡。交付 `qc` 模块，规则集 `QC-IMPACT@1.0.0`。

## 与批次模块的边界

批次冻结（`QC_FAILURE` 等原因由授权人声明）属 batch 模块，已于 DEV-013 交付。本模块**经 `IBatchStatusPort` 消费**批次的存在性与非冻结状态（gate-then-commit：端口在自身事务外调用，决策与版本原样固定进 QC 运行事实），不复制也不放宽批次语义。

## 五关口与偏差获批的分离

| 关口（LAB-QC-003） | 说明 |
|---|---|
| `INVESTIGATION` | 调查记录引用 |
| `IMPACT_SCOPE` | 影响范围确认 |
| `VALIDITY_DECISION` | 结果有效性决定 |
| `ADOPTION_RULE` | 采用规则确认 |
| `TECHNICAL_REVIEW` | 技术复核 |

`DEVIATION_APPROVAL` **刻意不在此列**（RULE-010：偏差获批不等于结果可报告）。它是独立的追加事实，可以任意累积，`QcRules.OutstandingGates` 从不查看它——因此偏差获批在结构上无法解除阻断。

## 传播语义

- QC 运行失败时必须一次性登记覆盖全批的影响集；**空影响集被拒绝**——那正是 RULE-022 禁止的"只处理发现异常的那条结果"的捷径。
- 影响集内任一目标在解除前一律 `BLOCKED` 并列明未满足关口（LAB-QC-002、AC-QC-001）。
- 解除为单向一次性事实；解除后影响目标转为 `ALLOWED`。
- 全部事实追加式，DB 触发器强制（`QC.QC_APPEND_ONLY` / errcode 55000）；乐观并发用 `expectedCurrentVersion` + advisory lock；事实、平台审计与发件箱同事务。

## 状态机

```
OPEN ──(全 PASS)──> PASSED
  └──(任一 FAIL)──> FAILED ──(影响集 + 五关口齐备)──> RELEASED
```

## 明确不在本卡范围

环境监控采集与校准状态权威来源（OD-012 未决）、分包方 QC 回传（OD-013 未决）、报告签发闸门（OD-011/022/029 未决）、QC 限值统计与控制图趋势判定。

## 可报告性端口的作用域

`IQcReportabilityPort` 回答的是"**在这一个 QC 运行看来**，该目标是否可报告"——请求同时固定 `qcRunId` 与 `targetId`。被多个运行触及的目标，只有在**每个**运行都回答 ALLOWED 时才可报告；因此消费方（报告链后续卡）必须逐个询问涉及该目标的运行。PASSED 运行对任意目标回答 ALLOWED，是因为从未失败的运行不扣留任何东西，也无权代表另一个运行的阻断发言。

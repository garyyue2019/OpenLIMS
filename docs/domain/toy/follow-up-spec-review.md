# 玩具后续规格评审与批准记录

本文件记录 `0.1.0 proposed` 草案的评审导航和后续人工批准结果，不是 PRD 来源。权威产品来源仍为 `docs/AI原生第三方产品检测LIMS产品需求文档.md`，机器执行源仍为 `spec/`；最终批准证据写入对应 `1.0.0 approved` 规格。

## 2026-07-28 批准结果

用户明确声明“DEV-025/026 现在 approved”，批准本文件所列 DEV-025/026 草案语义、权限边界、评审项与 `allowed_paths`。受控流程据此发布 BUS-TOY-003/004/005、AC-TOY-003/004、ATC-TOY-002/003 的 `1.0.0 approved` 后继版本。

本次批准不包含 DEV-027、BUS-TOY-006、完整 AC-TOY-002 或 OD-034；它们继续保持 `proposed/open/BLOCKED`。

## 草案拆分

| 顺序 | Story | 实施任务 | 范围 | 当前 readiness |
|---|---|---|---|---|
| 1 | `ATC-TOY-002@1.0.0` | `DEV-025` | OPS-TOY-004/006：TestUnit 危险域、平行、序列、互斥破坏与样品需求技术批准 | `READY`：2026-07-28 人工批准 |
| 2 | `ATC-TOY-003@1.0.0` | `DEV-026` | OPS-TOY-007：工件版本、市场/语言/图片证据、LabelReview 失效与重审 | `READY`：2026-07-28 人工批准 |
| 3 | `ATC-TOY-004@0.1.0` | `DEV-027` | OPS-TOY-005：多 TestUnit 危险域覆盖与产品/型号结论 | `BLOCKED`：除草案批准外，还必须先解决 OD-034 |

PRD 的 `AC-TOY-002` 同时覆盖互斥 TestUnit 和多 TestUnit 汇总结论。为避免删除后半段的阻断语义：

- `AC-TOY-002@0.1.0` 完整保留原组合验收，并依赖 `OD-034@0.1.0`；
- `AC-TOY-003@0.1.0` 是 OPS-TOY-004/006 的可独立评审切片；
- DEV-025 不声称交付完整 AC-TOY-002，也不生成 ConformityDecision。

## DEV-025 已批准选择

1. 危险域代码和版本的权威来源：范围、方法还是独立规则对象；不得由运行时代码自带默认枚举。
2. 非破坏共享允许条件及 `shareRuleRef@version` 的批准主体。
3. `toy.sample-demand.approve` 能力归属，以及起草人与批准人是否必须职责分离。
4. 化学最低取样量、复测预留和留样的规则来源与单位/维度契约。
5. 本草案提出的原则：互斥破坏组对同一 TestUnit 的历史禁用不因 Allocation 释放而解除。

## DEV-026 已批准选择

1. `reviewScopeRefs` 与产品/年龄变化 `changeScopeRefs` 的权威匹配规则和版本所有者。
2. `toy.label.manage` 与 `toy.label.review` 的角色映射及职责分离要求。
3. 当影响规则为 UNKNOWN 时，旧审查按失败关闭阻断是否符合业务期望。
4. 产品版本引用使用 toy 聚合版本的边界，以及后续产品主数据版本落地后的迁移策略。
5. 产品合规工件由 toy 模块拥有；既有 labeling 模块继续只负责收样标签打印/扫描，不共享私表。

## DEV-027 必须先解决 OD-034

以下内容不得由 AI 或实现代理补默认值：

- “单项符合”“已测范围符合”及全面法规符合的准确定义和允许措辞；
- 各结论层级的批准权限、职责分离和签署要求；
- 外部/另行授权全面评估、认证状态和证书的引用与责任边界；
- 未检测、未覆盖、UNKNOWN 和不适用项的强制披露；
- 报告样例、反向场景和行业措辞批准。

在 `OD-034` 发布后继 `approved/decided` 版本前，报告链现有 `RPT.CONFORMITY_DECISION_UNAVAILABLE` 必须保持，不能实现 toy 结论端口、迁移或放行逻辑。

## 已执行批准流程

1. 已保留 `0.1.0 proposed` 草案历史。
2. 已由用户提供批准证据，并为通过的 BUS、AC 和 Story 发布 `1.0.0 approved` 后继文件；AI 不是批准主体。
3. Story 的 `depends_on` 已固定到批准后的精确 `ID@1.0.0`，`readiness` 已改为 `ready`，最终 `allowed_paths` 已冻结。
4. 运行：

   ```powershell
   python -m tools.specgen validate --strict-warnings
   python -m tools.specgen source-status
   python -m tools.specgen impact
   python -m tools.specgen ready --story ATC-TOY-002@1.0.0
   python -m tools.specgen ready --story ATC-TOY-003@1.0.0
   ```

5. 仅对输出 `READY` 的 DEV-025/026 编码；`ATC-TOY-004` 还需先完成 OD-034 和后继 MAJOR 规格评审。

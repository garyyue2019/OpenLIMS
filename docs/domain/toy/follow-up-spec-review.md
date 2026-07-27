# 玩具后续规格评审清单

本文件是 `0.1.0 proposed` 草案的评审导航，不是 PRD 来源，也不构成批准证据。权威产品来源仍为 `docs/AI原生第三方产品检测LIMS产品需求文档.md`，机器执行源仍为 `spec/`。

## 草案拆分

| 顺序 | Story | 实施任务 | 范围 | 当前 readiness |
|---|---|---|---|---|
| 1 | `ATC-TOY-002@0.1.0` | `DEV-025` | OPS-TOY-004/006：TestUnit 危险域、平行、序列、互斥破坏与样品需求技术批准 | `BLOCKED`：等待 BUS-TOY-003/004、AC-TOY-003 和 Story 人工批准 |
| 2 | `ATC-TOY-003@0.1.0` | `DEV-026` | OPS-TOY-007：工件版本、市场/语言/图片证据、LabelReview 失效与重审 | `BLOCKED`：等待 BUS-TOY-005、AC-TOY-004 和 Story 人工批准 |
| 3 | `ATC-TOY-004@0.1.0` | `DEV-027` | OPS-TOY-005：多 TestUnit 危险域覆盖与产品/型号结论 | `BLOCKED`：除草案批准外，还必须先解决 OD-034 |

PRD 的 `AC-TOY-002` 同时覆盖互斥 TestUnit 和多 TestUnit 汇总结论。为避免删除后半段的阻断语义：

- `AC-TOY-002@0.1.0` 完整保留原组合验收，并依赖 `OD-034@0.1.0`；
- `AC-TOY-003@0.1.0` 是 OPS-TOY-004/006 的可独立评审切片；
- DEV-025 不声称交付完整 AC-TOY-002，也不生成 ConformityDecision。

## DEV-025 需要人工确认

1. 危险域代码和版本的权威来源：范围、方法还是独立规则对象；不得由运行时代码自带默认枚举。
2. 非破坏共享允许条件及 `shareRuleRef@version` 的批准主体。
3. `toy.sample-demand.approve` 能力归属，以及起草人与批准人是否必须职责分离。
4. 化学最低取样量、复测预留和留样的规则来源与单位/维度契约。
5. 本草案提出的原则：互斥破坏组对同一 TestUnit 的历史禁用不因 Allocation 释放而解除。

## DEV-026 需要人工确认

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

## 建议批准流程

1. 评审并修订 `0.1.0 proposed` 草案；任何语义变化继续保留为草案。
2. 由有权人员记录批准证据，并为通过的 BUS、AC 和 Story 发布 `1.0.0 approved` 后继文件；不要把 AI 作为批准主体。
3. Story 的 `depends_on` 全部改为批准后的精确 `ID@1.0.0`，`readiness` 改为 `ready`，并冻结最终 `allowed_paths`。
4. 运行：

   ```powershell
   python -m tools.specgen validate --strict-warnings
   python -m tools.specgen source-status
   python -m tools.specgen impact
   python -m tools.specgen ready --story ATC-TOY-002@1.0.0
   python -m tools.specgen ready --story ATC-TOY-003@1.0.0
   ```

5. 仅对输出 `READY` 的卡编码；`ATC-TOY-004` 还需先完成 OD-034 和后继 MAJOR 规格评审。

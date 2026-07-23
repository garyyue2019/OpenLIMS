# OpenLIMS AI 开发规格与需求编译指南

本目录说明如何把产品 PRD 变成可审阅、可生成、可增量同步、可交给 AI 开发且不会静默改写历史的工程规格。

## 当前交付内容

```text
OpenLIMS/
├─ docs/
│  ├─ AI原生第三方产品检测LIMS产品需求文档.md   # 产品叙述来源，只读
│  ├─ ai-development/                          # 本操作手册
│  └─ decision-packets/                        # 人工联合评审包和数据采集模板
├─ spec/
│  ├─ specgen.json                             # 编译器配置和治理策略
│  ├─ source-baseline.json                     # 已审阅 PRD 来源指纹
│  ├─ schema/                                  # JSON Schema
│  ├─ decisions/                               # OD/工程决策候选
│  ├─ requirements/                            # 结构化需求
│  ├─ rules/、nfr/、acceptance/                # 规则、非功能、验收
│  ├─ stories/                                 # AI Task Card 的权威 JSON
│  ├─ releases/                                # 精确版本发布基线
│  ├─ baselines/                               # 人工保存的锁快照
│  └─ seals/                                   # 已批准发布的不可覆盖 Seal
├─ generated/spec/                             # 纯生成目录，禁止手改
├─ tools/specgen/                              # Python 标准库编译器
├─ scripts/spec.ps1、spec.sh                   # 命令包装
├─ tests/                                      # 自动化测试
├─ AGENTS.md                                   # AI 仓库规则
└─ .github/workflows/spec-governance.yml       # CI 门禁
```

## 核心原则

1. **AI 负责起草，确定性工具负责编译。** CI 中不调用 AI。
2. **PRD 变化不会直接改代码。** 它先触发 source-drift，人工完成语义评审和版本变更后才生成。
3. **生成目录禁止手改。** 工具比较实际文件树、期望内容和 lock，不只相信 manifest。
4. **业务代码永不自动覆盖。** 契约和验收变化通过失败测试迫使代码同步。
5. **运行语义固定版本。** 所有依赖和发布均使用 `ID@x.y.z`；禁止 `latest`。
6. **历史不可变。** 封存版本、数据库历史迁移、验收证据和已完成任务只追加，不覆盖。
7. **未知默认阻断。** `activation.applicability=UNKNOWN` 不能自动进入生产。
8. **生成不等于批准。** 当前样例均为 `proposed/in_review/blocked`，与 PRD“待联合评审”状态一致。

## 最常用命令

```powershell
# 校验机器规格
python -m tools.specgen validate

# 检查 PRD 是否发生尚未审阅的变化
python -m tools.specgen source-status

# 计算规格与来源变化的直接/传递影响
python -m tools.specgen impact

# 重新生成派生物
python -m tools.specgen generate

# CI 使用：只读检查，绝不改工作区
python -m tools.specgen check

# 检查某张任务卡是否可交给 AI
python -m tools.specgen ready --story ATC-REC-003@0.1.0

# 只读检查评审记录、对象哈希和技术锁是否闭合（当前预期 BLOCKED）
python -m tools.specgen review-status --change-set CHANGE-PLT-NEXT-VERSIONS-001

# 自动化测试
python -m unittest discover -s tests -p "test_*.py" -v
```

## 文档导航

- [01 架构与所有权](01-architecture-and-ownership.md)
- [02 需求变更与同步流程](02-change-workflow.md)
- [03 结构化规格编写规范](03-spec-authoring.md)
- [04 CLI 命令参考](04-cli-reference.md)
- [05 AI 开发执行与验收](05-ai-development-process.md)
- [06 Release 1 Backlog 拆解](06-release1-backlog.md)
- [07 发布、Seal 与历史治理](07-release-and-history.md)
- [08 故障排查](08-troubleshooting.md)
- [09 推行清单](09-rollout-checklist.md)
- [Release 1 决策冲刺评审包](../decision-packets/REL-R1-DECISION-SPRINT-001.md)
- [ED-001 技术栈与工程仓库候选方案](../decision-packets/ED-001-TECH-STACK-CANDIDATE.md)
- [ATC-PLT-000 工程骨架联合评审说明](../decision-packets/ATC-PLT-000-ENGINEERING-SKELETON-REVIEW.md)
- [ATC-PLT-000 联合评审与依赖裁剪审批包](../decision-packets/ATC-PLT-000-JOINT-APPROVAL-PACKET.md)
- [ATC-PLT-000 下一Major版本精确变更集](../decision-packets/ATC-PLT-000-NEXT-VERSION-CHANGESET.md)
- [ATC-PLT-000 生成任务卡](../../generated/spec/tasks/ATC-PLT-000__v0.1.0.md)
- [ATC-PLT-000 下一Major生成任务卡](../../generated/spec/tasks/ATC-PLT-000__v1.0.0.md)

## Release 1 当前决策输入

- 用户已接受“3岁及以上常规硬质塑胶非电动玩具”的候选资格/排除边界，仍待责任角色正式批准和真实订单验证；
- 用户已接受 `R1.0中国内销 → 后续欧盟 → 后续美国`，中国内销Requirements Profile仍待形成和批准；
- 用户已选择分析化学为Release 1唯一主技术包，物理机械后移；方法/QC/仪器证据和正式批准仍未闭合；
- 微生物/生物已明确排除 Release 1；
- 容量已规范为日均 500 订单，峰值、对象倍率、并发和附件增长仍待采集；
- 广东华瑾及测试中心按虚拟沙箱场景处理，不能作为真实付费灯塔或生产上线证据；
- 技术栈无硬性限制，`ED-001@1.0.0`中的`.NET 10 + Vue 3 + PostgreSQL 18`仍是`PENDING_REVIEW_FOR_ENGINEERING_SKELETON_ONLY`，15项精确版本锁均待核验。
- 工程骨架规格 `ATC-PLT-000@0.1.0` 与收窄后的 `ATC-PLT-000@1.0.0` 评审投影均为 `proposed/blocked`；新版只依赖工程、安全、审计、NFR和部署验收草案，不再夹带`OD-020/OD-025`业务/生产批准。
- 本轮新增的15个`1.0.0`规格仅供评审、影响分析和生成预览，不存在新`approved/decided/ready`对象，也不授权实现任何检测业务或工程骨架。

决策包目录同时提供容量、方法、场景、玩具资格、分析化学取样映射、分析化学QC、市场协议、责任角色和平台联合评审记录九份 CSV 模板。所有业务/生产证据必须标记 `SYNTHETIC`、`REAL_DEIDENTIFIED` 或 `VERIFIED_PRODUCTION`，不得混用；评审记录还必须包含受控身份、授权范围、对象哈希、时间和签名/批准引用。

## 当前样例边界

当前共有 59 个规格版本和 14 张 AI Task Card：旧`0.1.0`与新`1.0.0`各包含1张`ATC-PLT-000`工程骨架草案及6张“收样—身份—隔离—异常—放行”纵向切片任务。它们用于演示版本并存、依赖裁剪和同步机制，但不是完整 Release 1 backlog，也不是生产批准。

其余 PRD 条目仍被扫描和监控：当前来源清单包含 389 个带 ID 条目。未精化条目不会被自动解释为接口或代码，以免 AI 在缺少业务决策时猜测。

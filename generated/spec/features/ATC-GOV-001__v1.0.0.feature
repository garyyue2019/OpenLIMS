# GENERATED FILE — DO NOT EDIT. openlims-specgen@0.1.0
# Source: ATC-GOV-001@1.0.0
# Spec-Fingerprint: a0b1e6f5d4519b03c508bc33457107bb75e228dd2623e6d4313e7cfe2274b3c2
Feature: ATC-GOV-001 实施 DEV-019 冻结 R1 适用性基线
  R1 适用性从分散在各规格 activation 字段的隐式状态升级为单一冻结基线：任何未来卡启用新行业包、技术包或未决 OD 能力都必须先通过后继决策版本与新快照，防止试点范围静默扩张（OD-001 反范围蔓延目标）。

  @generated @atc-gov-001 @positive
  Scenario: TC-GOV-001-01 OD-001 决策记录
    Given 用户 2026-07-26 决定
    When validate/ready 运行
    Then OD-001@1.0.0 approved+decided
    And approval_evidence 含用户原话
    And v0.1.0 原样保留
    And 依赖 OD-001@1.0.0 的故事不再因决策未闭合阻断

  @generated @atc-gov-001 @regression
  Scenario: TC-GOV-001-02 适用性基线断言
    Given traceability 生成物
    When 仓库契约测试运行
    Then 全部 approved core 规格 ENABLED
    And BUS-TEX-001..005 enabled_pack/DISABLED
    And BUS-AI-001..003 conditional/DISABLED
    And 无 approved 规格为 UNKNOWN

  @generated @atc-gov-001 @regression
  Scenario: TC-GOV-001-03 快照冻结
    Given r1-applicability-baseline.lock.json
    When 仓库契约测试运行
    Then 快照存在且为合法 JSON
    And 含 OD-001@1.0.0 与 BUS-GOV-001@1.0.0、ATC-GOV-001@1.0.0
    And 与当前生成锁的对应条目一致

  @generated @atc-gov-001 @negative
  Scenario: TC-GOV-001-04 快照不可覆盖
    Given 快照已存在
    When 再次 snapshot 同名
    Then specgen 报错拒绝覆盖

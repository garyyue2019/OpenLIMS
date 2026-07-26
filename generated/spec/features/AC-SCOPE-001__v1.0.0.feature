# GENERATED FILE — DO NOT EDIT. openlims-specgen@0.1.0
# Source: AC-SCOPE-001@1.0.0
# Spec-Fingerprint: 0a563aaed59835af2a2796f6b679a9bdb5998f2272ac7f6bc28f8b60ea0e17fe
Feature: AC-SCOPE-001 ScopeLine 完整链与生产资格门禁
  完整批准范围行可由固定版本重建并获得生产资格；缺失、越权、旧版本或未知语义统一失败关闭。

  @generated @prd-acceptance
  Scenario: AC-SCOPE-001 ScopeLine 完整链与生产资格门禁
    Given 调用人具有 scope.approve 及对象范围
    And 提交的矩阵版本包含完整 ScopeLine 引用
    And EvaluationMode 条件字段满足批准基线
    When 提交初始或后继批准版本并查询固定版本生产资格
    Then 原子创建不可变 APPROVED 矩阵版本、范围行、审计和 Outbox
    And 完整当前版本返回 ALLOWED 和固定规则版本
    And EVALUATED 可重建限值与判定规则引用
    And 非 EVALUATED 不伪造符合性决定
    And 缺失引用、候选、越权、并发冲突、旧版本和 UNKNOWN 均阻断且无生产副作用

# GENERATED FILE — DO NOT EDIT. openlims-specgen@0.1.0
# Source: AC-TRACE-001@1.0.0
# Spec-Fingerprint: a99b23ae81a0272b3475e3039ddb33873632d237772355446dc1ec6ec5867f5f
Feature: AC-TRACE-001 全链追溯
  已签发报告行必须能从报告行完整重建到要求选择快照的全部贡献链；任一必需输入缺失、重复归属、循环或未解释歧义时不得签发；EVALUATED 模式还必须追溯 ConformityDecision、限值与决策规则。

  @generated @prd-acceptance
  Scenario: AC-TRACE-001 全链追溯
    Given 一个报告行采用了聚合自三个平行试样的结果
    And 该采用经 ResultAdoptionPort 以 ALLOWED 与精确组版本固定
    And 各试样各自具备批次、分配、收样与范围行引用
    When 重建该报告行的贡献链并评估签发门禁
    Then 从报告行 → 当前采用结果 → 采用规则版本 → 各来源结果组 → 批次与执行记录 → 试样分配 → 收样项与身份 → 范围行 → 要求选择快照，全链可完整重建
    And 任一必需引用缺失即阻断签发并指明缺失环节
    And 同一范围行+采用目标重复成行（重复归属）被拒绝
    And EVALUATED 模式的报告行因 ConformityDecision 依赖未决 OD-034 而一律阻断，返回明确原因而非默认放行

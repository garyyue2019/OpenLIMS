# GENERATED FILE — DO NOT EDIT. openlims-specgen@0.1.0
# Source: AC-TEXTILE-001@1.0.0
# Spec-Fingerprint: 5bf4aeacf8ab8b43294e12aac43287935dfd0280736a36d9aeb4ccca9d3125e6
Feature: AC-TEXTILE-001 样品不足与互斥裁样契约验收
  同一面料两个互斥破坏项目、三个平行加复测预留，可用面积不足时按款色、部位、方向和项目计算缺口并阻断；互斥任务不得共享同一裁片；本验收在纯契约层以确定性测试表达。

  @generated @prd-acceptance
  Scenario: AC-TEXTILE-001 样品不足与互斥裁样契约验收
    Given 同一块面料的两个互斥破坏项目需求行
    And 每行三个平行并含复测预留和留样
    And 该款色部位的可用面积不足以满足全部需求
    When 以纯规则计算样品需求与充足性
    And 尝试声明跨互斥组的裁片共享
    And 以未知方向或规则集版本重复计算
    Then 结果为 INSUFFICIENT 且缺口按款号、颜色、部件、部位聚合并列明方向与项目
    And 跨互斥组共享被拒绝，不得以同一裁片满足互斥任务
    And 非破坏性同规格行允许共享试样
    And 未知方向或规则集版本返回 UNKNOWN 并等同阻断
    And 序列化往返保持字段与形状冻结

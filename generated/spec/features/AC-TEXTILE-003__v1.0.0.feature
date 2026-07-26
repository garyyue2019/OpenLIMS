# GENERATED FILE — DO NOT EDIT. openlims-specgen@0.1.0
# Source: AC-TEXTILE-003@1.0.0
# Spec-Fingerprint: 7f112061a2b3ad9a52422aee2d72bcce4735fe0e80d57f8cbd6637382cdd2ecb
Feature: AC-TEXTILE-003 裁样方向与预处理超差契约验收
  方法要求按经纬方向裁切并规定温湿度调湿时，实际方向缺失或调湿超差必须关联来源布批、CuttingPlan、生成试样、计划/实际条件和超差影响；未获批准前报告不允许。本验收在纯契约层以确定性测试表达。

  @generated @prd-acceptance
  Scenario: AC-TEXTILE-003 裁样方向与预处理超差契约验收
    Given 一条要求距布边、经纬方向裁切并按规定温湿度调湿的预处理记录
    And 记录关联来源布批、CuttingPlan 和生成试样
    And 实际温度超出显式公差
    When 以纯规则评估计划与实际条件
    And 在缺少批准引用时检查报告许可
    And 补充批准引用后重新评估
    Then 评估为 OUT_OF_TOLERANCE 并逐字段列明计划值、实际值和偏差
    And 无批准引用时 reportingAllowed=false
    And 补充批准引用后 reportingAllowed=true 且超差事实保留
    And 方向未知或必录字段缺失即校验失败
    And 序列化往返保持字段与形状冻结

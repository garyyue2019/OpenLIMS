# GENERATED FILE — DO NOT EDIT. openlims-specgen@0.1.0
# Source: AC-BILL-001@1.0.0
# Spec-Fingerprint: 8d0d6511cad865448ba2e96f28ef1d4d31233860ca490344914d553a917ef269
Feature: AC-BILL-001 防重复计费
  相同服务事实、合同基线、收费维度和规则版本重复触发两次，系统只能存在一条有效计费证据。

  @generated @prd-acceptance
  Scenario: AC-BILL-001 防重复计费
    Given 结果组已有有效采用（服务完成事实）
    And 相同合同基线引用、收费维度和规则版本
    When 顺序重复提交两次相同计费证据
    And 并发提交两次相同计费证据
    And 报告重发或接口重试再次触发
    Then 只存在一条有效计费证据
    And 重复提交被拒绝且无副作用
    And 并发提交最多一笔成功
    And 调整不产生第二条同键证据

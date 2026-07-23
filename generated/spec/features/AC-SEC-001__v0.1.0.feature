# GENERATED FILE — DO NOT EDIT. openlims-specgen@0.1.0
# Source: AC-SEC-001@0.1.0
# Spec-Fingerprint: e762dae769c4ace3605cf0488a14788a88592bc9cc5b5cdce0c505786fb935bb
Feature: AC-SEC-001 集团内多维越权防护
  同一集团内未获授权的法人、实验室或客户对象不可被列表、搜索、导出、附件或 AI 查询泄露。

  @generated @prd-acceptance
  Scenario: AC-SEC-001 集团内多维越权防护
    Given 用户只获得法人甲、实验室甲和客户甲授权
    And 同一集团内存在未授权的法人乙、实验室乙或客户乙对象
    When 用户通过列表、搜索、导出、对象存储链接或 AI 查询尝试访问未授权对象
    Then 系统不返回未授权数据且不泄露对象是否存在
    And 系统记录安全审计
    And 存在明确跨机构授权时也只返回授权范围内的最小数据

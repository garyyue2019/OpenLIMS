# GENERATED FILE — DO NOT EDIT. openlims-specgen@0.1.0
# Source: AC-SEC-001@1.0.0
# Spec-Fingerprint: da7cd0c5db0a0540e0aa7f94a22b8e7b4e6a0b02cb0613e3eea87f02569b6a54
Feature: AC-SEC-001 集团内收样多维越权防护
  同一集团内未获授权的法人、实验室、客户或委托不得通过 DEV-003 收样命令和查询泄露。

  @generated @prd-acceptance
  Scenario: AC-SEC-001 集团内收样多维越权防护
    Given 用户只获得法人甲、实验室甲、客户甲和指定委托的收样授权
    And 同一集团内存在未授权的法人乙、实验室乙、客户乙或其他委托
    When 用户提交到货登记或查询登记结果
    Then 系统不返回或创建未授权数据且不泄露对象是否存在
    And 系统记录不含敏感正文的安全审计
    And 存在显式跨实验室授权时也只允许该委托范围内的最小操作

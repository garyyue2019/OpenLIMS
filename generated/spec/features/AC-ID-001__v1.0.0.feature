# GENERATED FILE — DO NOT EDIT. openlims-specgen@0.1.0
# Source: AC-ID-001@1.0.0
# Spec-Fingerprint: 8bc40f09987ef1b224e34bd4ee098049d37811b442a6027f33ccf6919fad6f88
Feature: AC-ID-001 身份评估三层事实和冲突事件
  身份评估保留声明、观察和结论历史；错配或待定发布冲突事实但不提前实现异常审批。

  @generated @prd-acceptance
  Scenario: AC-ID-001 身份评估三层事实和冲突事件
    Given 客户声明快照和 ReceivedItem 已存在
    And 评估员具有产品类别及对象组织范围权限
    When 评估员追加观察并提交 MATCHED、MISMATCHED 或 INDETERMINATE
    Then 三层事实分离且历史不可覆盖
    And 关键差异不能提交 MATCHED
    And 错配和待定原子发布幂等冲突事实 Outbox
    And 对象仍保持 QUARANTINED
    And 权限、必填证据和期望版本在服务端校验

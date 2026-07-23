# GENERATED FILE — DO NOT EDIT. openlims-specgen@0.1.0
# Source: AC-ID-001@0.1.0
# Spec-Fingerprint: a56a740130e68e3e26f1faad1a4b4141503968e5a8485bb94b6084565a227d70
Feature: AC-ID-001 身份错配
  合同要求与实物标签或观察不一致时创建异常并阻止正常接收，只有授权决定可以定义后续动作。

  @generated @prd-acceptance
  Scenario: AC-ID-001 身份错配
    Given 合同要求型号A
    And 实物标签和实验室观察指向型号C
    When 身份评估员提交不一致结论
    Then 系统创建身份冲突异常
    And 系统禁止正常接收
    And 只有OD-005批准矩阵中的授权决定可选择后续动作
    And 客户声明、观察和结论均保留

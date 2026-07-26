# GENERATED FILE — DO NOT EDIT. openlims-specgen@0.1.0
# Source: ATC-PLT-001@1.0.0
# Spec-Fingerprint: 013e485b4c1285652c986b4e460e2b8b9991c15fafa32e438276000d62cefa5e
Feature: ATC-PLT-001 实施 DEV-018 请求上下文与对象级授权正式化
  对象级授权与跨组织隔离从各模块分散测试升级为平台级组合证据：能力拒绝在真实链路中失败关闭、跨组织探测无法区分'不存在'与'无权访问'、correlation 全链可追，为审计与安全评审提供单一验证入口。

  @generated @atc-plt-001 @positive
  Scenario: TC-PLT-001-01 correlation 与身份贯穿
    Given 授权行为人与部署组织一致
    When 执行范围+数量命令并携带调用方 correlation
    Then platform.audit_intent 行固定 actor、组织与原样 correlation

  @generated @atc-plt-001 @negative
  Scenario: TC-PLT-001-02 能力拒绝失败关闭
    Given 范围授权端口 Deny，其余装配真实
    When 创建范围矩阵版本
    Then NOT_AUTHORIZED
    And 范围事实为零
    And scope.audit_attempt 恰一次且 correlation 原样
    And 无新增平台审计/发件箱

  @generated @atc-plt-001 @negative
  Scenario: TC-PLT-001-03 跨组织不泄露存在性
    Given 组织甲已有范围矩阵
    And 跨组织行为人（组织乙容器）
    When 读取组织甲对象与读取不存在对象
    Then 两者均 OBJECT_NOT_ACCESSIBLE，不可区分

  @generated @atc-plt-001 @negative
  Scenario: TC-PLT-001-04 组织不匹配失败关闭
    Given 行为人组织与部署组织不一致
    When 执行任一模块命令
    Then NOT_AUTHORIZED
    And 无业务事实

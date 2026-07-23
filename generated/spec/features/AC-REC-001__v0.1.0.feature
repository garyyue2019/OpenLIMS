# GENERATED FILE — DO NOT EDIT. openlims-specgen@0.1.0
# Source: AC-REC-001@0.1.0
# Spec-Fingerprint: 8e184359a694ee3fc02456e7df953eecfeff67ee8513e05e727855a1f65816e7
Feature: AC-REC-001 隔离控制
  身份评估未完成的实物尝试进入拆解、制样或检测分配时，服务端拒绝并留下完整阻断审计。

  @generated @prd-acceptance
  Scenario: AC-REC-001 隔离控制
    Given 收到的实物已登记且尚未完成身份评估
    And 用户已通过身份认证
    When 用户尝试创建拆解任务、制样记录或检测对象分配中的任一操作
    Then 服务端拒绝操作
    And 业务对象和数量不发生变化
    And 审计事件记录对象、用户、规则、动作、当前状态、阻断原因和关联ID

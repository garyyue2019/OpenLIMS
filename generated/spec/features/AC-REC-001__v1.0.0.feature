# GENERATED FILE — DO NOT EDIT. openlims-specgen@0.1.0
# Source: AC-REC-001@1.0.0
# Spec-Fingerprint: 9d4fdb3bf6b842a8a7c74fb01fd886bdec5cb10e81857043a51678fdeb030a9a
Feature: AC-REC-001 隔离资格统一失败关闭
  同一 ReceivedItem 对拆解、制样和检测分配得到一致阻断，不产生业务副作用并保留审计。

  @generated @prd-acceptance
  Scenario: AC-REC-001 隔离资格统一失败关闭
    Given ReceivedItem 已登记且保持 QUARANTINED
    And 调用身份已通过认证
    When 分别查询拆解、制样和检测对象分配资格
    Then 三个动作均返回 BLOCKED
    And 返回固定规则和对象版本
    And 不产生下游业务副作用
    And 每次尝试追加阻断审计
    And 未知、版本冲突、端口不可用和跨组织请求均失败关闭

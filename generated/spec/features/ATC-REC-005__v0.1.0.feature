# GENERATED FILE — DO NOT EDIT. openlims-specgen@0.1.0
# Source: ATC-REC-005@0.1.0
# Spec-Fingerprint: 0cbee64dcc5595dfe1f7ea0405f09d696489b7c60b74079d30e55b0ed0cdba00
Feature: ATC-REC-005 处理收样异常并执行授权决定
  收样异常有明确责任方、证据、影响和授权决定；系统不会为了推进订单而静默缩小范围、延长有效期或默认接受风险。

  @generated @atc-rec-005 @positive
  Scenario: TC-REC-005-01 数量不足
    Given 实物数量低于批准样品需求
    When 收样员创建异常
    Then 异常保持开放
    And 不得自动缩减范围
    And 提供补样或范围变更受控路径

  @generated @atc-rec-005 @negative
  Scenario: TC-REC-005-02 无证据条件接收
    Given 超温异常缺少温度记录和技术影响评估
    When 尝试条件接收
    Then 返回DECISION_EVIDENCE_INCOMPLETE
    And 保持隔离

  @generated @atc-rec-005 @boundary
  Scenario: TC-REC-005-03 未知适用性
    Given 异常类型未配置批准矩阵
    When 提交决定
    Then 返回APPLICABILITY_UNKNOWN
    And 默认阻断并告警

  @generated @atc-rec-005 @security
  Scenario: TC-REC-005-04 越权批准
    Given 用户不在该严重度审批范围
    When 批准条件接收
    Then 服务端拒绝
    And 状态不变
    And 记录越权尝试

  @generated @atc-rec-005 @concurrency
  Scenario: TC-REC-005-05 并发决定
    Given 两名批准人读取同一版本
    When 分别提交冲突决定
    Then 最多一笔成功
    And 另一笔版本冲突

  @generated @atc-rec-005 @recovery
  Scenario: TC-REC-005-06 通知恢复
    Given 决定已提交但下游通知暂时失败
    When 发件箱重试
    Then 决定不重复
    And 下游只生效一次
    And 差异队列关闭

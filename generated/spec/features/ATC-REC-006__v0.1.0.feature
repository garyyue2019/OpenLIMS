# GENERATED FILE — DO NOT EDIT. openlims-specgen@0.1.0
# Source: ATC-REC-006@0.1.0
# Spec-Fingerprint: ad0c4e4db8854b5a743b731bfbe231494c5d6e6081a4276ea6c8c2f71b00377d
Feature: ATC-REC-006 受控解除隔离并发布执行资格
  只有身份、异常和限制均满足批准规则的实物才获得明确执行资格；在制下游工作固定引用该资格版本，后续变化通过撤销或新决定传播。

  @generated @atc-rec-006 @positive
  Scenario: TC-REC-006-01 正常放行
    Given 身份匹配
    And 无开放阻断异常
    And 授权有效
    When 提交放行
    Then 创建ReleaseDecision
    And 状态变为ACCEPTED
    And 发布一次幂等事件

  @generated @atc-rec-006 @boundary
  Scenario: TC-REC-006-02 受限放行
    Given OD-005允许条件接收
    And 限制完整且批准链满足
    When 提交受限放行
    Then 状态为CONDITIONALLY_ACCEPTED
    And 事件携带限制摘要
    And 下游按动作再次校验

  @generated @atc-rec-006 @negative
  Scenario: TC-REC-006-03 开放异常
    Given 存在未决定的身份冲突异常
    When 尝试放行
    Then 返回BLOCKING_EXCEPTION_OPEN
    And 状态和事件均不变

  @generated @atc-rec-006 @security
  Scenario: TC-REC-006-04 越权放行
    Given 用户无对应实验室或严重度授权
    When 提交放行
    Then 服务端拒绝
    And 记录审计

  @generated @atc-rec-006 @concurrency
  Scenario: TC-REC-006-05 并发异常出现
    Given 预览时可放行
    And 提交前新增阻断异常
    When 使用旧对象版本提交
    Then 版本冲突或条件写入失败
    And 不发布事件

  @generated @atc-rec-006 @recovery
  Scenario: TC-REC-006-06 重复事件
    Given 同一outbox事件投递两次
    When 下游消费者处理
    Then 资格投影只生效一次
    And 处理记录可审计

  @generated @atc-rec-006 @regression
  Scenario: TC-REC-006-07 规则升级
    Given 对象已绑定旧ReleaseDecision
    And 新OD-005版本发布
    When 读取在制任务资格
    Then 继续引用旧决定
    And 除非批准影响评估和迁移

# GENERATED FILE — DO NOT EDIT. openlims-specgen@0.1.0
# Source: ATC-REC-005@2.0.0
# Spec-Fingerprint: 00c62c57cf6c5e054bb6502a53a4b64e923d82be8f51b9ce6774fbfb3a72c234
Feature: ATC-REC-005 实施 DEV-006 收样异常与授权决定
  收样和身份异常具有不可变事实、明确责任、最小授权决定和可审计限制，系统不会为了推进订单自动接受风险。

  @generated @atc-rec-005 @positive
  Scenario: TC-REC-005-01 普通异常建档
    Given 授权收样员发现数量不足
    When 创建异常
    Then 保存 STANDARD 异常
    And 保持隔离

  @generated @atc-rec-005 @boundary
  Scenario: TC-REC-005-02 受限条件接收
    Given 普通异常证据和技术影响完整
    When 质量负责人提交非空允许/禁止动作和有效期
    Then 决定保存
    And 不解除隔离

  @generated @atc-rec-005 @negative
  Scenario: TC-REC-005-03 空限制条件接收
    Given 限制或有效期缺失
    When 提交条件接收
    Then 拒绝且状态不变

  @generated @atc-rec-005 @permission
  Scenario: TC-REC-005-04 污染必须 EHS
    Given 污染异常
    When 质量负责人尝试批准后由 EHS 安全封存
    Then 首次拒绝并审计
    And EHS 决定成功

  @generated @atc-rec-005 @security
  Scenario: TC-REC-005-05 发起人自批
    Given 创建者同时具有批准能力
    When 批准自己的异常
    Then 职责分离拒绝

  @generated @atc-rec-005 @concurrency
  Scenario: TC-REC-005-06 并发决定
    Given 两人读取同一版本
    When 提交冲突决定
    Then 最多一笔成功
    And 另一笔版本冲突

  @generated @atc-rec-005 @recovery
  Scenario: TC-REC-005-07 未知分类
    Given 分类不在矩阵
    When 创建或决定
    Then UNKNOWN 并保持隔离

  @generated @atc-rec-005 @transaction
  Scenario: TC-REC-005-08 原子回滚与幂等恢复
    Given 审计或 Outbox 失败
    When 提交决定并重试
    Then 首笔全部回滚
    And 重试只产生一个逻辑决定

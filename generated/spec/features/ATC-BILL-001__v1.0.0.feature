# GENERATED FILE — DO NOT EDIT. openlims-specgen@0.1.0
# Source: ATC-BILL-001@1.0.0
# Spec-Fingerprint: b6802a9d2521ecbc38a2b2c7adf6cf51b0b026252ae9cfc40864e0f0d870bb58
Feature: ATC-BILL-001 实施 DEV-015 唯一计费事实
  每个合同约定的服务完成事实产生且仅产生一条有效计费证据；报告重发、接口重试或并发触发不会造成重复计费，免费项与更正全程可审计。

  @generated @atc-bill-001 @positive
  Scenario: TC-BILL-001-01 采用门禁与首条证据
    Given 结果组有效采用且端口 ALLOWED
    When 提交计费证据
    Then 证据创建且固定采用目标
    And 审计与 Outbox 同事务提交

  @generated @atc-bill-001 @negative
  Scenario: TC-BILL-001-02 AC-BILL-001 顺序防重复
    Given 相同四元组已有证据
    When 重复提交
    Then BIL.DUPLICATE_BILLING
    And 只存在一条有效证据

  @generated @atc-bill-001 @concurrency
  Scenario: TC-BILL-001-03 并发防重复
    Given 两个调用相同四元组
    When 并发提交
    Then 最多一笔成功
    And 唯一约束兜底

  @generated @atc-bill-001 @boundary
  Scenario: TC-BILL-001-04 零金额原因
    Given 免费项
    When 零金额带原因与不带原因提交；非零带零金额原因提交
    Then 带原因成功
    And 缺原因与错配拒绝

  @generated @atc-bill-001 @permission
  Scenario: TC-BILL-001-05 越权
    Given 缺少 capability 或对象范围
    When 任一操作
    Then 统一拒绝
    And 追加脱敏失败审计

  @generated @atc-bill-001 @negative
  Scenario: TC-BILL-001-06 门禁失败关闭
    Given 采用端口 BLOCKED/UNKNOWN
    When 提交证据
    Then 失败关闭且无事实

  @generated @atc-bill-001 @recovery
  Scenario: TC-BILL-001-07 原子回滚
    Given 审计或 Outbox 失败
    When 提交并重试
    Then 首笔全部回滚
    And 重试只产生一条证据

  @generated @atc-bill-001 @regression
  Scenario: TC-BILL-001-08 调整链与不可变历史
    Given 已有证据
    When 追加正负调整、尝试零额调整和改写历史
    Then 调整链保留且引用原证据
    And 零额调整拒绝
    And 数据库拒绝 UPDATE/DELETE

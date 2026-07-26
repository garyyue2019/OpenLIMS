# GENERATED FILE — DO NOT EDIT. openlims-specgen@0.1.0
# Source: ATC-RESULT-001@1.0.0
# Spec-Fingerprint: 89ae098800fcc9e3929738cf6a9978e0a79567d6cda65f661882c816343c0124
Feature: ATC-RESULT-001 实施 DEV-014 结果来源与采用
  实验室的每个报告结果都可追溯到不可变原始观测与外部证据；复测不能被用来挑选有利结果，采用决定在看到复测数据前就被规则锁定，下游报告只消费唯一有效采用结果。

  @generated @atc-result-001 @positive
  Scenario: TC-RES-001-01 建组与初测观测
    Given 批次门禁 ALLOWED
    When 建组并提交 INITIAL 观测与证据
    Then 组 v1→v2，证据哈希与解析器版本固定
    And 审计与 Outbox 同事务提交

  @generated @atc-result-001 @boundary
  Scenario: TC-RES-001-02 来源图约束
    Given 组内两条观测
    When 提交含排除输入的派生、重复计入与悬空输入
    Then 合法派生成功且排除理由保留
    And 重复计入与悬空输入被拒绝

  @generated @atc-result-001 @negative
  Scenario: TC-RES-001-03 AC-RETEST-001 预先规则
    Given 组内已有 INITIAL 且无采用规则
    When 直接提交 RETEST 观测
    Then RES.ADOPTION_RULE_REQUIRED
    And 无副作用

  @generated @atc-result-001 @negative
  Scenario: TC-RES-001-04 策略校验反挑选
    Given RETEST_REPLACES_ORIGINAL 规则与更有利的 INITIAL
    When 尝试采用 INITIAL
    Then RES.ADOPTION_STRATEGY_VIOLATION
    And 采用最新 RETEST 成功

  @generated @atc-result-001 @permission
  Scenario: TC-RES-001-05 越权
    Given 缺少 capability 或对象范围
    When 任一操作
    Then 统一拒绝
    And 追加脱敏失败审计

  @generated @atc-result-001 @concurrency
  Scenario: TC-RES-001-06 并发版本冲突
    Given 两个调用使用相同 expectedCurrentVersion
    When 并发提交观测
    Then 最多一笔成功
    And 另一笔版本冲突

  @generated @atc-result-001 @recovery
  Scenario: TC-RES-001-07 原子回滚
    Given 审计或 Outbox 失败
    When 提交并重试
    Then 首笔全部回滚
    And 重试只产生一个逻辑事实

  @generated @atc-result-001 @regression
  Scenario: TC-RES-001-08 唯一有效采用与不可变历史
    Given 两次合规采用
    When 查询采用状态并尝试改写历史
    Then 最新采用版本有效且历史保留
    And 数据库拒绝 UPDATE/DELETE
    And 旧组版本状态查询 UNKNOWN

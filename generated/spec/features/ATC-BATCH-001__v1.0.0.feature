# GENERATED FILE — DO NOT EDIT. openlims-specgen@0.1.0
# Source: ATC-BATCH-001@1.0.0
# Spec-Fingerprint: 6028422fb5b08d932fa1a3d310e0513b436edee083e38cb2ab109cfcda6df3af
Feature: ATC-BATCH-001 实施 DEV-013 制备/分析批最小切片
  实验室可以把跨委托的试样与批准 QC 组成责任清晰的类型化批次；QC 失败时整批影响被系统性冻结且不可选择性重开，原始数据权威保留在源系统并以不可变引用可追溯。

  @generated @atc-batch-001 @positive
  Scenario: TC-BATCH-001-01 类型化批次与分配门禁成员
    Given 分配状态端口 ALLOWED
    When 创建分析批并添加试样成员与 QC 样
    Then 批次 ACTIVE 且成员固定端口决定与归属
    And 审计与 Outbox 同事务提交

  @generated @atc-batch-001 @boundary
  Scenario: TC-BATCH-001-02 跨委托客户隔离
    Given 三个不同委托的试样成员
    When 入批并读取
    Then 每个成员保留自身客户/委托归属
    And 批级字段不覆盖成员归属

  @generated @atc-batch-001 @negative
  Scenario: TC-BATCH-001-03 门禁失败关闭
    Given 分配端口 BLOCKED/UNKNOWN 或异常
    When 添加试样成员
    Then 失败关闭且无事实
    And 原因与来源记录

  @generated @atc-batch-001 @negative
  Scenario: TC-BATCH-001-04 重复入批与未知枚举
    Given 同一分配已入批或未知类型/原因
    When 再次提交
    Then BAT.VALIDATION_FAILED
    And 无副作用

  @generated @atc-batch-001 @permission
  Scenario: TC-BATCH-001-05 越权
    Given 缺少 capability 或法人/实验室范围
    When 任一操作
    Then 统一拒绝
    And 追加脱敏失败审计

  @generated @atc-batch-001 @concurrency
  Scenario: TC-BATCH-001-06 并发版本冲突
    Given 两个调用使用相同 expectedCurrentVersion
    When 并发添加成员
    Then 最多一笔成功
    And 另一笔版本冲突

  @generated @atc-batch-001 @recovery
  Scenario: TC-BATCH-001-07 原子回滚
    Given 审计或 Outbox 失败
    When 提交并重试
    Then 首笔全部回滚
    And 重试只产生一个逻辑事实

  @generated @atc-batch-001 @regression
  Scenario: TC-BATCH-001-08 AC-BATCH-001 整批冻结
    Given 含三委托成员与 QC 的批次
    And QC 失败
    When 冻结后尝试新增成员/证据、改写历史和查询状态
    Then 整批冻结且原数据保留
    And 新增被拒、数据库拒绝改写
    And 状态 BLOCKED，不得选择性重开

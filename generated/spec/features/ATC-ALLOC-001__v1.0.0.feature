# GENERATED FILE — DO NOT EDIT. openlims-specgen@0.1.0
# Source: ATC-ALLOC-001@1.0.0
# Spec-Fingerprint: d8e63c13c3459ba2c9811cdfaa36fa55b2741d4fc49af2bc0bf746678e525ca1
Feature: ATC-ALLOC-001 实施 DEV-010 任务分配资格
  实验室在创建任何任务使用前，必须获得身份/隔离、范围资格和数量可用性三重版本固定许可；破坏性使用互斥、并发超分配和一切未知语义被系统性阻断，下游可用公共端口验证分配的精确版本状态。

  @generated @atc-alloc-001 @positive
  Scenario: TC-ALLOC-001-01 三端口全 ALLOWED 创建分配
    Given 引用完整且授权有效
    And 三端口均 ALLOWED
    When 创建分配
    Then 创建 ACTIVE 分配并固定三端口决定与版本
    And 审计与 Outbox 同事务提交
    And 状态查询 ALLOWED

  @generated @atc-alloc-001 @boundary
  Scenario: TC-ALLOC-001-02 非破坏性并存与破坏性互斥
    Given 同一对象已有活跃非破坏性分配
    When 再创建非破坏性分配、创建破坏性分配、之后再创建任意分配
    Then 非破坏性可并存
    And 破坏性创建成功后同对象新分配返回 ALC.DESTRUCTIVE_CONFLICT
    And 释放破坏性分配后恢复

  @generated @atc-alloc-001 @negative
  Scenario: TC-ALLOC-001-03 任一端口 BLOCKED 失败关闭
    Given Receiving、Scope 或 Quantity 端口返回 BLOCKED
    When 创建分配
    Then ALC.ELIGIBILITY_BLOCKED 且记录来源端口
    And 不产生事实或成功事件

  @generated @atc-alloc-001 @negative
  Scenario: TC-ALLOC-001-04 UNKNOWN 与过期失败关闭
    Given 端口返回 UNKNOWN 或 validUntil 已过期
    When 创建分配
    Then ALC.APPLICABILITY_UNKNOWN 或 ALC.ALLOCATION_EXPIRED
    And 无副作用

  @generated @atc-alloc-001 @permission
  Scenario: TC-ALLOC-001-05 越权
    Given 缺少 capability 或对象范围
    When 创建、释放或查询
    Then 统一拒绝
    And 追加脱敏失败审计

  @generated @atc-alloc-001 @concurrency
  Scenario: TC-ALLOC-001-06 并发分配冲突
    Given 两个调用使用相同 expectedCurrentVersion
    When 并发创建同对象分配
    Then 最多一笔成功
    And 另一笔版本冲突
    And 对象分配版本只推进一次

  @generated @atc-alloc-001 @recovery
  Scenario: TC-ALLOC-001-07 原子回滚
    Given 审计或 Outbox 失败
    When 创建并重试
    Then 首笔全部回滚
    And 重试只创建一个逻辑分配

  @generated @atc-alloc-001 @regression
  Scenario: TC-ALLOC-001-08 不可变历史与追加释放
    Given 存在 ACTIVE 分配
    When 尝试改写历史、重复释放和查询旧版本状态
    Then 数据库拒绝 UPDATE/DELETE
    And 释放至多一次
    And 旧对象分配版本状态查询 UNKNOWN

# GENERATED FILE — DO NOT EDIT. openlims-specgen@0.1.0
# Source: ATC-SCP-001@1.0.0
# Spec-Fingerprint: 1ae1eaf0c359f3c045dddcb33fd10e0b2543e9b8f68aa7631577fa791be20503
Feature: ATC-SCP-001 实施 DEV-008 ScopeLine 生产可用门禁
  授权技术人员可以把完整检测范围固定为不可变批准版本；任何下游在创建生产事实前可用公共端口验证精确矩阵版本是否具备生产资格。

  @generated @atc-scp-001 @positive
  Scenario: TC-SCP-001-01 完整初始版本
    Given 全部引用完整
    And 授权有效
    When 提交 v1
    Then 创建 APPROVED@v1
    And 资格 ALLOWED

  @generated @atc-scp-001 @boundary
  Scenario: TC-SCP-001-02 评价模式条件
    Given 包含四种 EvaluationMode
    When 提交版本
    Then 仅 EVALUATED 要求限值与判定规则
    And 其他模式保存各自依据

  @generated @atc-scp-001 @negative
  Scenario: TC-SCP-001-03 缺失或冲突引用
    Given 缺少必需引用或结论字段与模式冲突
    When 提交版本
    Then 稳定错误
    And 不创建事实或成功事件

  @generated @atc-scp-001 @negative
  Scenario: TC-SCP-001-04 候选失败关闭
    Given 仅有客户、套餐、BOM 或 AI 候选
    When 查询生产资格
    Then BLOCKED 或 UNKNOWN
    And 无生产副作用

  @generated @atc-scp-001 @permission
  Scenario: TC-SCP-001-05 越权
    Given 缺少 capability 或对象范围
    When 提交或读取
    Then 统一拒绝
    And 追加脱敏失败审计

  @generated @atc-scp-001 @concurrency
  Scenario: TC-SCP-001-06 并发修订
    Given 两个调用使用相同 expectedCurrentVersion
    When 并发提交
    Then 仅一个创建后继版本
    And 另一个版本冲突

  @generated @atc-scp-001 @recovery
  Scenario: TC-SCP-001-07 原子回滚
    Given 审计或 Outbox 失败
    When 提交并重试
    Then 首笔全部回滚
    And 重试只创建一个逻辑版本

  @generated @atc-scp-001 @regression
  Scenario: TC-SCP-001-08 不可变历史与旧版本
    Given v2 已批准
    When 读取 v1 或尝试修改历史
    Then v1 可只读重建
    And 旧版本生产资格 UNKNOWN
    And 数据库拒绝改写

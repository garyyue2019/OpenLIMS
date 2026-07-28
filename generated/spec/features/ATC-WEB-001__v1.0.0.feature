# GENERATED FILE — DO NOT EDIT. openlims-specgen@0.1.0
# Source: ATC-WEB-001@1.0.0
# Spec-Fingerprint: 8e37afc7bc27fbcd69cbc2abc5e171a47f8a2cc6d9714f3c0c5ae2d84aef323e
Feature: ATC-WEB-001 实施 Scope、Quantity、Allocation 与 Batch 实验室工作台
  实验室操作员能够在一个经过身份认证、可键盘操作且错误可恢复的工作台中完成范围矩阵、数量账户、样品分配和批次的创建与查询；每一步展示服务器返回的精确对象版本、资格、可用量、状态和阻断原因，不再依赖直接调用 API 或阅读数据库。

  @generated @atc-web-001 @positive
  Scenario: TC-WEB-001-01 四模块主流程
    Given 已认证且服务器依次返回范围、数量、分配和批次成功响应
    When 操作员提交每一步并打开结果
    Then 导航和四个页面可用
    And 精确版本传递到下一步
    And 成功只由响应驱动

  @generated @atc-web-001 @negative
  Scenario: TC-WEB-001-02 Problem Details 可恢复错误
    Given API 返回稳定 errorCode、correlationId 和 nextAction
    When 写请求失败
    Then 显示安全错误详情
    And 保留非敏感输入
    And 不显示成功

  @generated @atc-web-001 @boundary
  Scenario: TC-WEB-001-03 版本和数量边界
    Given 版本为零/小数或数量非正
    When 操作员提交
    Then 客户端阻止明显无效输入
    And 不发送请求

  @generated @atc-web-001 @permission
  Scenario: TC-WEB-001-04 未认证和无权限
    Given 会话匿名、过期或 API 返回 403
    When 进入路由或执行操作
    Then 引导登录或显示无权
    And 不泄露对象存在性

  @generated @atc-web-001 @recovery
  Scenario: TC-WEB-001-05 网络失败后显式重试
    Given 首次网络请求失败
    When 用户确认后重试
    Then 不自动重复写操作
    And 表单内容保留
    And 成功响应后才更新详情

  @generated @atc-web-001 @regression
  Scenario: TC-WEB-001-06 Receiving 兼容
    Given 现有 Receiving 路由和导航
    When 注册四个新 feature
    Then Receiving 路由和测试保持通过
    And 无重复 route/navigation ID

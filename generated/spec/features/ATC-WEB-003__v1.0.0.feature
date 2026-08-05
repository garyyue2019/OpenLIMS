# GENERATED FILE — DO NOT EDIT. openlims-specgen@0.1.0
# Source: ATC-WEB-003@1.0.0
# Spec-Fingerprint: 6581fa2f4d8344db3f30a1e45c09ca55b5d88baf035f5f210157feec78bd7a13
Feature: ATC-WEB-003 实施 Billing 与 Labeling Web 工作台
  计费操作员可在报告和结果采用后的商业闭环中创建并核对唯一计费证据、追加不可变调整并查看服务端状态；收样和样品操作员可从独立导航入口处理现有对象的打印、任务查询、受控重印和扫码校验，不再依赖刚完成的收样登记页面或直接调用 API。

  @generated @atc-web-003 @positive
  Scenario: TC-WEB-003-01 Billing 与 Labeling 主流程
    Given 已认证且服务器返回 8 个操作的成功响应
    When 操作员创建、调整、查询计费证据并创建、查询、重印和扫描标签
    Then 两个导航和页面可用
    And 成功只由响应驱动
    And 精确版本与状态可见

  @generated @atc-web-003 @negative
  Scenario: TC-WEB-003-02 BLOCKED 与 UNKNOWN 失败关闭
    Given 计费状态 BLOCKED/UNKNOWN 或打印任务 UNKNOWN
    When 操作员查看结果或尝试后续动作
    Then 显示服务器原因
    And 不伪造允许或送达
    And UNKNOWN 不提供普通重试

  @generated @atc-web-003 @boundary
  Scenario: TC-WEB-003-03 版本、金额、对象类型和原因边界
    Given 版本非正整数、调整为零、对象类型不支持或重印原因为空
    When 操作员提交
    Then 客户端阻止明显无效输入
    And 不发送请求

  @generated @atc-web-003 @permission
  Scenario: TC-WEB-003-04 未认证、无能力与服务端拒绝
    Given 会话匿名、缺 UX capability 或 API 返回 403
    When 进入路由或执行操作
    Then 引导登录或显示只读无权
    And 不泄露对象存在性
    And 不显示成功

  @generated @atc-web-003 @recovery
  Scenario: TC-WEB-003-05 网络失败后显式重试
    Given 首次写请求网络失败
    When 用户确认后重试
    Then 不自动重复写操作
    And 非敏感输入保留
    And 成功响应后才更新详情

  @generated @atc-web-003 @audit
  Scenario: TC-WEB-003-06 错误关联与敏感信息保护
    Given API 返回 errorCode 和 correlationId
    When 页面呈现问题
    Then 显示支持关联信息
    And 不显示令牌、可信身份、完整扫码载荷或打印机地址

  @generated @atc-web-003 @regression
  Scenario: TC-WEB-003-07 现有 Web 功能兼容
    Given Receiving 与两批实验室工作台已注册
    When 注册 Billing 与 Labeling
    Then 所有既有路由和测试保持通过
    And 无重复 route 或 navigation ID

# GENERATED FILE — DO NOT EDIT. openlims-specgen@0.1.0
# Source: ATC-WEB-002@1.0.0
# Spec-Fingerprint: 0959d0fd44400fbafe0d7227dd301a42792b5f4232f8ec0d1bef40b43cd1fc77
Feature: ATC-WEB-002 实施 Instrument、Result、QC 与 Report 实验室工作台
  实验室操作员可以在同一套经过身份认证、键盘可操作且错误可恢复的 Web 工作台中，从批次后的仪器证据开始，完成结果来源和采用、QC 判定与五门放行、报告组装门禁以及受控签发和版本验证，不再依赖直接调用 API 或读取数据库。

  @generated @atc-web-002 @positive
  Scenario: TC-WEB-002-01 四模块主流程
    Given 已认证且服务器依次返回 Instrument、Result、QC、Report 成功响应
    When 操作员完成导入、采用、放行和签发并重新读取对象
    Then 导航和四个页面可用
    And 精确版本和证据传递到下一步
    And 成功只由响应驱动

  @generated @atc-web-002 @negative
  Scenario: TC-WEB-002-02 阻断链失败关闭
    Given 导入异常、结果未采用、QC 未释放或报告门禁未满足
    When 操作员尝试后续动作
    Then 显示服务器阻断原因
    And 不伪造后续成功
    And 输入和查询结果保持可恢复

  @generated @atc-web-002 @boundary
  Scenario: TC-WEB-002-03 版本、数值和 JSON 边界
    Given 版本为小数/负数、必需数量非正或结构化输入不是期望对象/数组
    When 操作员提交
    Then 客户端阻止明显无效输入
    And 不发送请求

  @generated @atc-web-002 @permission
  Scenario: TC-WEB-002-04 未认证、无能力提示和服务端拒绝
    Given 会话匿名、缺 UX capability 或 API 返回 403
    When 进入路由或执行操作
    Then 引导登录或显示只读/无权
    And 服务端拒绝不被覆盖
    And 不泄露对象存在性

  @generated @atc-web-002 @recovery
  Scenario: TC-WEB-002-05 网络失败后显式重试
    Given 首次网络请求失败
    When 用户确认后重试
    Then 不自动重复写操作
    And 非敏感表单内容保留
    And 成功响应后才更新详情

  @generated @atc-web-002 @audit
  Scenario: TC-WEB-002-06 问题关联与敏感信息保护
    Given API 返回稳定 errorCode 和 correlationId
    When 错误面板呈现失败
    Then 显示可支持关联信息
    And 不显示令牌、可信身份或原始文件内容

  @generated @atc-web-002 @regression
  Scenario: TC-WEB-002-07 现有 Web 功能兼容
    Given 现有 Receiving 和第一批实验室工作台路由与导航
    When 注册第二批工作台
    Then 所有现有路由和测试保持通过
    And 无重复 route/navigation ID

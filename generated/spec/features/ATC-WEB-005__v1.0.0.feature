# GENERATED FILE — DO NOT EDIT. openlims-specgen@0.1.0
# Source: ATC-WEB-005@1.0.0
# Spec-Fingerprint: 85f847e4431215f274104cde879728edc9e2558be67967f0dc1bfd56b249aeb6
Feature: ATC-WEB-005 实施 Toy 全流程 Web 工作台
  玩具技术人员可在稳定、认证、错误可恢复的 Web 工作台中完成产品年龄与可及性链、TestUnit 与样品需求链、标签工件和审核链，并由具备相应结论批准能力的人员创建两级固定结论；每一步展示精确版本、证据、阻断和未覆盖项。

  @generated @atc-web-005 @positive
  Scenario: TC-WEB-005-01 Toy 四链主流程
    Given 已认证且 19 个操作返回成功
    When 完成产品、TestUnit、标签审核和两级结论操作
    Then 四个页面可用
    And 精确版本/证据可见
    And 成功只由响应驱动

  @generated @atc-web-005 @negative
  Scenario: TC-WEB-005-02 跨链 UNKNOWN 失败关闭
    Given 年龄、样品需求、下游分配、标签影响或结论证据任一 UNKNOWN
    When 查看或尝试后续动作
    Then 原因可见
    And 不伪造允许、VALID、APPROVED 或结论

  @generated @atc-web-005 @boundary
  Scenario: TC-WEB-005-03 版本、枚举、证据和数组边界
    Given 版本非法、阶段/范围/类型非法、证据数组为空或哈希无效
    When 提交
    Then 客户端阻止明显无效输入
    And 不发送请求

  @generated @atc-web-005 @permission
  Scenario: TC-WEB-005-04 六种能力分离
    Given 仅有部分 Toy capability 或匿名
    When 执行不同组操作
    Then 缺失能力动作禁用或引导登录
    And 服务端拒绝不被覆盖

  @generated @atc-web-005 @concurrency
  Scenario: TC-WEB-005-05 精确并发版本
    Given 服务端返回 expected version conflict
    When 用户提交旧版本
    Then 冲突和 correlationId 可见
    And 不自动改用最新版重试

  @generated @atc-web-005 @recovery
  Scenario: TC-WEB-005-06 网络失败显式重试
    Given 首次请求网络失败
    When 用户显式重试
    Then 输入保留
    And 不自动重复写入
    And 成功后才更新

  @generated @atc-web-005 @audit
  Scenario: TC-WEB-005-07 签署关联与敏感信息保护
    Given 结论 API 返回 errorCode/correlationId
    When 页面呈现
    Then 关联信息可见
    And 令牌、Secret、图像内容和可信身份不可见

  @generated @atc-web-005 @regression
  Scenario: TC-WEB-005-08 现有 Web 兼容
    Given 现有 Receiving、实验室、Billing/Labeling、Textile 路由
    When 注册 Toy 四路由
    Then 既有测试通过
    And 无重复 route/navigation ID

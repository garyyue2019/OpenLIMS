# GENERATED FILE — DO NOT EDIT. openlims-specgen@0.1.0
# Source: ATC-WEB-004@1.0.0
# Spec-Fingerprint: fe9bdf7aec5ea3648984b7b1f1f0f8ee66839378e7be666b0f3d2fd062af9bd3
Feature: ATC-WEB-004 实施 Textile Web 工作台
  纺织技术人员可从稳定 Web 入口提交完整版本化需求，查看 SUFFICIENT、INSUFFICIENT 或 UNKNOWN 的试样分量和面积缺口，创建绑定需求版本/哈希的 CuttingPlan，并由具备明确批准能力的人员冻结计划，不再直接调用 API。

  @generated @atc-web-004 @positive
  Scenario: TC-WEB-004-01 Textile 主流程
    Given 已认证且需求 SUFFICIENT
    When 计算需求、创建计划、批准并查询
    Then 4 个操作成功
    And 精确版本/哈希传递
    And APPROVED 只来自响应

  @generated @atc-web-004 @negative
  Scenario: TC-WEB-004-02 INSUFFICIENT 与 UNKNOWN 失败关闭
    Given 服务端返回面积不足或未知
    When 展示并尝试后续操作
    Then 原因和缺口可见
    And 不伪造可批准状态

  @generated @atc-web-004 @boundary
  Scenario: TC-WEB-004-03 版本、方向、尺寸与数组边界
    Given 版本非正、未知方向、尺寸非正或需求数组为空
    When 提交
    Then 客户端阻止明显无效输入
    And 不发送请求

  @generated @atc-web-004 @permission
  Scenario: TC-WEB-004-04 管理与批准能力分离
    Given 仅有 manage 或匿名
    When 尝试批准或进入页面
    Then 批准禁用或引导登录
    And 服务端拒绝不被覆盖

  @generated @atc-web-004 @recovery
  Scenario: TC-WEB-004-05 网络失败显式重试
    Given 首次请求网络失败
    When 用户显式重试
    Then 输入保留
    And 不自动重复写入
    And 成功后才更新

  @generated @atc-web-004 @audit
  Scenario: TC-WEB-004-06 错误关联与敏感信息保护
    Given API 返回 errorCode/correlationId
    When 页面呈现
    Then 关联信息可见
    And 令牌和可信身份不可见

  @generated @atc-web-004 @regression
  Scenario: TC-WEB-004-07 现有 Web 兼容
    Given 既有 Receiving、实验室和业务工作台
    When 注册 Textile
    Then 既有路由测试通过
    And 无重复 route/navigation ID

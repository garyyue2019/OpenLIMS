# GENERATED FILE — DO NOT EDIT. openlims-specgen@0.1.0
# Source: ATC-PLT-003@1.0.0
# Spec-Fingerprint: b2a7af44a3db6fa8f6c7893009b53f1a6cfc5dc4de0c644f80eadd1f7f9218fa
Feature: ATC-PLT-003 建立业务模块接入与验证通道
  后续 AI 开发任务可以在固定边界内新增业务模块并由 API、Worker、Web、迁移和验证入口显式接入，不再复制平台脚手架或临时绕过架构门禁。

  @generated @atc-plt-003 @positive
  Scenario: TC-PLT-003-01 positive
    Given 一个 tests 下的合法夹具模块
    When API 和 Worker 组合该模块
    Then 模块只注册一次
    And 测试端点与后台服务可解析
    And 生产 Host 不出现夹具路由

  @generated @atc-plt-003 @boundary
  Scenario: TC-PLT-003-02 boundary
    Given 两个模块声明相同 moduleId 或 schemaName
    When 构建模块清单
    Then 稳定失败
    And 不得静默覆盖先注册模块

  @generated @atc-plt-003 @architecture
  Scenario: TC-PLT-003-03 architecture
    Given 模块项目尝试引用另一模块私有实现或 DbContext
    When 运行架构门禁
    Then 测试失败并指出非法依赖

  @generated @atc-plt-003 @recovery
  Scenario: TC-PLT-003-04 recovery
    Given 模块存在待执行迁移
    When API 或 Worker 正常启动
    Then 不自动修改 Schema
    And readiness 按依赖真实状态返回

  @generated @atc-plt-003 @frontend
  Scenario: TC-PLT-003-05 frontend
    Given 合法前端功能清单
    When 组合路由和导航
    Then 确定性生成结果
    And 重复 featureId、路由名或路径被拒绝

  @generated @atc-plt-003 @security
  Scenario: TC-PLT-003-06 security
    Given 测试模块尝试提交或覆盖另一 OrganizationGroup
    When 请求进入 Host
    Then 沿用平台稳定错误拒绝
    And 不切换集团上下文

  @generated @atc-plt-003 @regression
  Scenario: TC-PLT-003-07 regression
    Given DEV-002 完整变更
    When 运行 Windows、Linux、后端、前端和规格门禁
    Then 现有平台测试全部通过
    And 相同规格输入第二次生成 written=0

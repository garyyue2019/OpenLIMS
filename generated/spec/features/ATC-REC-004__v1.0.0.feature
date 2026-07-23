# GENERATED FILE — DO NOT EDIT. openlims-specgen@0.1.0
# Source: ATC-REC-004@1.0.0
# Spec-Fingerprint: 8b686110ac21fadea29341ac61345d31642298c9f3952204889ee2a144d35ce6
Feature: ATC-REC-004 记录身份证据并形成匹配或冲突结论
  实验室可以证明收到的究竟是什么、依据是什么以及与委托是否一致；身份冲突不会被操作员无依据地改成一致。

  @generated @atc-rec-004 @positive
  Scenario: TC-REC-004-01 身份一致
    Given 声明型号与实物标签和观察一致
    And 证据完整
    When 授权评估员提交MATCHED
    Then 保存三层记录
    And 建立实际身份映射
    And 对象仍等待受控放行

  @generated @atc-rec-004 @negative
  Scenario: TC-REC-004-02 身份错配
    Given 合同型号A
    And 观察型号C
    When 提交不一致结论
    Then 创建身份冲突异常
    And 禁止正常接收
    And 保留声明和观察

  @generated @atc-rec-004 @boundary
  Scenario: TC-REC-004-03 多义归属
    Given 实物可能对应两个委托项且证据不足
    When 尝试选择其中一个为MATCHED
    Then 拒绝无依据匹配
    And 保存INDETERMINATE并阻断

  @generated @atc-rec-004 @security
  Scenario: TC-REC-004-04 未授权评估
    Given 用户没有该产品类别身份评估授权
    When 提交观察或结论
    Then 服务端拒绝
    And 不产生结论
    And 记录审计

  @generated @atc-rec-004 @concurrency
  Scenario: TC-REC-004-05 并发更改
    Given 用户打开版本3
    And 另一人先提交版本4
    When 用户提交旧版本结论
    Then 返回EXPECTED_VERSION_CONFLICT
    And 不得覆盖版本4

  @generated @atc-rec-004 @recovery
  Scenario: TC-REC-004-06 异常事务原子性
    Given 错配结论需要创建异常
    And 异常持久化模拟失败
    When 提交错配
    Then 结论和状态均回滚
    And 重试后只创建一个异常

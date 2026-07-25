# GENERATED FILE — DO NOT EDIT. openlims-specgen@0.1.0
# Source: ATC-REC-003@2.0.0
# Spec-Fingerprint: 8f80f420f8587b216df5a594319f9eb27b9c76774a0eb96bc66b0050ef515326
Feature: ATC-REC-003 实施隔离门禁和 ReceivedItem 身份评估
  身份评估人员可以在集团多机构授权边界内证明收到的实物是什么以及为何一致、错配或待定；任何身份结论都不能绕过隔离进入实验室执行。

  @generated @atc-rec-003 @positive
  Scenario: TC-REC-003-01 完整一致身份
    Given 声明和观察关键字段一致
    When 授权评估员提交 MATCHED
    Then 三层事实保存
    And 对象仍隔离
    And 资格仍 BLOCKED

  @generated @atc-rec-003 @negative
  Scenario: TC-REC-003-02 错配不能伪装一致
    Given 声明型号 A、观察型号 C
    When 提交 MATCHED 后再提交 MISMATCHED
    Then MATCHED 被拒绝
    And MISMATCHED 原子保存并发布冲突事实

  @generated @atc-rec-003 @boundary
  Scenario: TC-REC-003-03 证据不足待定
    Given 存在多种可能身份
    When 提交 INDETERMINATE
    Then 保存待定结论
    And 保持隔离

  @generated @atc-rec-003 @permission
  Scenario: TC-REC-003-04 未授权和跨组织
    Given 缺少任一能力或组织范围
    When 读取或提交评估
    Then 统一拒绝
    And 不泄露对象
    And 脱敏审计

  @generated @atc-rec-003 @concurrency
  Scenario: TC-REC-003-05 并发版本冲突
    Given 两个评估员读取同一版本
    When 第二人用旧期望版本提交
    Then 返回版本冲突
    And 不覆盖首个结果

  @generated @atc-rec-003 @transaction
  Scenario: TC-REC-003-06 审计与 Outbox 原子性
    Given 模拟审计或 Outbox 失败
    When 提交错配结论
    Then 事实、结论和事件全部回滚
    And 重试只产生一个逻辑结论

  @generated @atc-rec-003 @contract
  Scenario: TC-REC-003-07 三个动作共享门禁
    Given 任一身份结论
    When 查询三个动作
    Then 均返回 BLOCKED
    And 规则和对象版本一致

  @generated @atc-rec-003 @recovery
  Scenario: TC-REC-003-08 未知失败关闭
    Given 未知规则或持久化不可用
    When 查询资格或提交评估
    Then 资格 UNKNOWN 并阻断或 API 503
    And 不使用过期允许缓存

  @generated @atc-rec-003 @deployment-isolation
  Scenario: TC-REC-003-09 集团不能由客户端选择
    Given 部署绑定集团甲
    When 客户端尝试提交集团乙
    Then 请求失败关闭
    And 不访问集团乙数据

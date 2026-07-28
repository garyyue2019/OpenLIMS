# GENERATED FILE — DO NOT EDIT. openlims-specgen@0.1.0
# Source: ATC-TOY-002@1.0.0
# Spec-Fingerprint: fe06ad5b5c30ab08e6e2b76434437a16a8e6b7d54940aa412a81aad6ddb19fe4
Feature: ATC-TOY-002 实施 DEV-025 玩具 TestUnit 计划与样品需求批准
  玩具任务在开始前就能证明使用了哪些 TestUnit、覆盖哪些危险域、为何需要这些样品量，以及哪些破坏试验不能共享实物；技术人员对自动计算承担明确批准责任，数量不足或规则未知不会被默认成零需求。

  @generated @atc-toy-002 @positive
  Scenario: TC-TOY-002-01 危险域、平行与有序序列
    Given 批准范围和样品规则版本完整
    When 创建含两个平行和多步骤的计划
    Then TestUnit 固定危险域/平行/连续序列
    And 计划和需求可重建

  @generated @atc-toy-002 @negative
  Scenario: TC-TOY-002-02 互斥破坏任务分离
    Given 两个任务属于同一互斥破坏组
    When 先分配到不同 TestUnit，再尝试复用同一 TestUnit
    Then 不同 TestUnit 成功
    And 复用以 TOY.DESTRUCTIVE_TEST_UNIT_CONFLICT 拒绝

  @generated @atc-toy-002 @recovery
  Scenario: TC-TOY-002-03 历史释放不解除互斥
    Given 互斥任务已使用并释放 TestUnit
    When 重试把同组任务分配给该 TestUnit
    Then 仍被拒绝
    And 历史证据保留

  @generated @atc-toy-002 @positive
  Scenario: TC-TOY-002-04 需求分量与技术批准
    Given 基础、化学最低、复测和留样规则完整
    When 计算并由技术人员批准
    Then 每个分量及来源独立可见
    And 批准版本冻结后可调用端口

  @generated @atc-toy-002 @boundary
  Scenario: TC-TOY-002-05 未知规则和单位冲突
    Given 缺化学最低规则或维度/单位冲突
    When 计算并尝试批准
    Then 返回 UNKNOWN
    And 不批准、不预留、不分配

  @generated @atc-toy-002 @permission
  Scenario: TC-TOY-002-06 技术批准权限
    Given 有 toy.manage 但无拟议批准能力的行为人
    When 批准需求
    Then TOY.NOT_AUTHORIZED
    And 业务事实为零且失败尝试留痕

  @generated @atc-toy-002 @concurrency
  Scenario: TC-TOY-002-07 并发计划版本
    Given 同一产品两个请求使用相同 expectedCurrentVersion
    When 并发追加计划
    Then 恰一个成功
    And 另一方 TOY.EXPECTED_VERSION_CONFLICT

  @generated @atc-toy-002 @negative
  Scenario: TC-TOY-002-08 下游端口失败关闭
    Given 需求已批准但 Quantity 返回不足或 Allocation 返回 UNKNOWN
    When 请求分配
    Then TOY.DOWNSTREAM_ELIGIBILITY_BLOCKED
    And 无半完成分配且决定版本留痕

  @generated @atc-toy-002 @audit
  Scenario: TC-TOY-002-09 审计或 Outbox 失败回滚
    Given 注入证据写入失败
    When 创建或批准
    Then 计划/批准与证据全部回滚
    And 独立失败尝试恰一条

# GENERATED FILE — DO NOT EDIT. openlims-specgen@0.1.0
# Source: ATC-REC-001@2.0.0
# Spec-Fingerprint: 5f5d2d7c7e2faf0dd30de7f3da41adc0d9370fd64eff4205b1b3d57f206f9a5a
Feature: ATC-REC-001 登记到货批、包装单元和收到实物
  收样员可以一次登记一笔到货，明确包装单元和逐个完整销售玩具或套装；后续身份、谱系和检测不再依赖含义混乱的单一 Sample 记录。

  @generated @atc-rec-001 @positive
  Scenario: TC-REC-001-01 正常登记
    Given 合法委托和授权收样员
    And 一个包装含两个完整玩具
    When 提交带幂等键的登记命令
    Then 创建一笔 Receipt、一个 Container、两个 ReceivedItem
    And 实物均为 QUARANTINED
    And 写入审计和 Outbox

  @generated @atc-rec-001 @idempotency
  Scenario: TC-REC-001-02 重复请求
    Given 首次请求已成功
    When 相同幂等键和相同载荷重试
    Then 返回首次结果
    And 对象数量不增加

  @generated @atc-rec-001 @negative
  Scenario: TC-REC-001-03 幂等冲突
    Given 幂等键已绑定另一载荷
    When 复用该键提交不同包装数据
    Then 返回 IDEMPOTENCY_CONFLICT
    And 没有新增对象

  @generated @atc-rec-001 @security
  Scenario: TC-REC-001-04 多维授权阻断
    Given 用户只获授权法人甲、实验室甲、客户甲和委托甲
    When 提交属于任一未授权维度的收样
    Then 服务端拒绝
    And 不泄露对象是否存在
    And 记录安全审计

  @generated @atc-rec-001 @recovery
  Scenario: TC-REC-001-05 事务原子性
    Given 审计或 Outbox 持久化模拟失败
    When 提交收样
    Then 业务对象全部回滚
    And 重试后只产生一套对象

  @generated @atc-rec-001 @security
  Scenario: TC-REC-001-06 集团上下文不可覆盖
    Given 部署绑定集团甲
    And 载荷包含集团乙标识
    When 提交收样
    Then 拒绝未知字段
    And 不切换集团上下文
    And 记录安全审计

  @generated @atc-rec-001 @authorization
  Scenario: TC-REC-001-07 显式跨实验室协作
    Given 用户具有指定委托的跨实验室收样授权
    When 提交收样
    Then 登记成功
    And 权限不扩散到其他委托
    And 审计保留各责任主体

  @generated @atc-rec-001 @boundary
  Scenario: TC-REC-001-08 实物强制拆分
    Given 两个玩具的型号、批次、序列号、颜色、包装、封识或实物状态至少一项不同
    When 提交为同一 ReceivedItem
    Then 返回 IDENTITY_GRANULARITY_UNRESOLVED
    And 整笔登记不产生半成品

  @generated @atc-rec-001 @concurrency
  Scenario: TC-REC-001-09 并发幂等登记
    Given 两个请求使用相同幂等键和相同载荷
    When 并发提交
    Then 只创建一套 Receipt、Container 和 ReceivedItem
    And 两个请求返回同一首次结果
    And 审计和 Outbox 不重复

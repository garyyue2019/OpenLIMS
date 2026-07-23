# GENERATED FILE — DO NOT EDIT. openlims-specgen@0.1.0
# Source: ATC-REC-001@0.1.0
# Spec-Fingerprint: 071aeb85adf1e36257baab4700efc2c7d38bf001a8c99597e2d29153cf412d57
Feature: ATC-REC-001 登记到货批、包装单元和收到实物
  收样员可以一次登记一笔到货，明确其包装单元和实际收到的实物；后续身份、谱系和检测不再依赖含义混乱的单一 Sample 记录。

  @generated @atc-rec-001 @positive
  Scenario: TC-REC-001-01 正常登记
    Given 合法委托和授权收样员
    And 一个包装含两个实际实物
    When 提交带幂等键的登记命令
    Then 创建一笔Receipt、一个Container、两个ReceivedItem
    And 实物均进入QUARANTINED
    And 写入审计与发件箱

  @generated @atc-rec-001 @idempotency
  Scenario: TC-REC-001-02 重复请求
    Given 首次请求已经成功
    When 使用相同幂等键和相同载荷重试
    Then 返回首次结果
    And 对象数量不增加

  @generated @atc-rec-001 @negative
  Scenario: TC-REC-001-03 幂等冲突
    Given 幂等键已绑定另一载荷
    When 复用该键提交不同包装数据
    Then 返回IDEMPOTENCY_CONFLICT
    And 没有新增对象

  @generated @atc-rec-001 @security
  Scenario: TC-REC-001-04 跨组织授权阻断
    Given 用户只获授权法人甲、实验室甲和客户甲
    And 服务委托属于未授权的法人乙、实验室乙或客户乙
    When 提交收样
    Then 服务端拒绝
    And 不泄露对象是否存在或业务信息
    And 记录安全审计

  @generated @atc-rec-001 @recovery
  Scenario: TC-REC-001-05 事务原子性
    Given 审计或发件箱持久化模拟失败
    When 提交收样
    Then 业务对象全部回滚
    And 重试后只产生一套对象

  @generated @atc-rec-001 @security
  Scenario: TC-REC-001-06 集团上下文不可覆盖
    Given 部署已绑定集团甲
    And 客户端载荷额外提交集团乙标识
    When 提交收样
    Then 拒绝未知或禁止字段
    And 不切换集团上下文
    And 记录安全审计

  @generated @atc-rec-001 @authorization
  Scenario: TC-REC-001-07 显式跨实验室协作
    Given 用户具有获批的跨实验室收样授权
    And 委托归属法人、收样实验室和执行实验室均已明确
    When 提交收样
    Then 登记成功
    And 只授予该委托范围内权限
    And 审计保留各责任主体和授权依据

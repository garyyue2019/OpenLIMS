# GENERATED FILE — DO NOT EDIT. openlims-specgen@0.1.0
# Source: ATC-REC-003@0.1.0
# Spec-Fingerprint: 98da53ec10f99c5bc56f5afe064e3874df1d88ca5220c9f6d99111876faf505c
Feature: ATC-REC-003 身份评估前实施统一隔离门禁
  身份未确认、被隔离、待定、拒收或安全封存的实物不能因页面遗漏、接口调用或并发竞争进入实验室执行。

  @generated @atc-rec-003 @negative
  Scenario: TC-REC-003-01 隔离状态阻断三个入口
    Given ReceivedItem状态为QUARANTINED
    When 分别尝试拆解、制样和检测分配
    Then 三个命令均返回RECEIVED_ITEM_NOT_RELEASED
    And 没有业务副作用
    And 各自记录阻断审计

  @generated @atc-rec-003 @boundary
  Scenario: TC-REC-003-02 未知状态失败关闭
    Given 规则不认识新的状态值或适用性为UNKNOWN
    When 请求进入执行
    Then 决策为UNKNOWN并按阻断处理
    And 产生配置告警

  @generated @atc-rec-003 @positive
  Scenario: TC-REC-003-03 允许状态
    Given 对象处于OD-005明确允许状态
    And 身份决定和限制满足
    When 创建下游命令
    Then 门禁返回ALLOWED
    And 下游可继续自身事务
    And 证据固定规则和对象版本

  @generated @atc-rec-003 @concurrency
  Scenario: TC-REC-003-04 状态并发变化
    Given 预检查时对象允许
    And 提交前对象被安全封存
    When 使用旧expectedItemVersion提交
    Then 条件写入失败
    And 不创建下游对象
    And 调用方必须重新评估

  @generated @atc-rec-003 @security
  Scenario: TC-REC-003-05 跨组织入口
    Given 调用方无实物所属法人、实验室或客户授权
    When 请求资格并提交下游命令
    Then 统一拒绝
    And 不泄露对象状态
    And 记录安全审计

  @generated @atc-rec-003 @recovery
  Scenario: TC-REC-003-06 依赖不可用
    Given receiving eligibility端口暂时不可用
    When 创建制样
    Then 失败关闭且标记可重试
    And 不使用过期允许缓存
    And 恢复后重新完整校验

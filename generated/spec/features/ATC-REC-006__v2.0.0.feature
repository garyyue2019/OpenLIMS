# GENERATED FILE — DO NOT EDIT. openlims-specgen@0.1.0
# Source: ATC-REC-006@2.0.0
# Spec-Fingerprint: fdf0bc2308e1e56cdd289ce94001b8e0971678fa3f00ca7329024cdfa1685087
Feature: ATC-REC-006 实施 DEV-007 受控放行与版本固定资格
  质量人员可以用一次受控操作释放身份匹配且风险已处理的实物；下游只依据固定放行决定获得正常或受限动作资格。

  @generated @atc-rec-006 @positive
  Scenario: TC-REC-006-01 无异常正常放行
    Given 身份 MATCHED
    And 无异常
    And 授权有效
    When 提交放行
    Then 形成 RELEASED
    And 状态为 ACCEPTED
    And v2 三动作 ALLOWED

  @generated @atc-rec-006 @boundary
  Scenario: TC-REC-006-02 多异常受限放行
    Given 全部异常有效条件接收
    When 提交放行
    Then 允许取交集、禁止取并集
    And 状态为 CONDITIONALLY_ACCEPTED
    And v2 按动作限制

  @generated @atc-rec-006 @negative
  Scenario: TC-REC-006-03 阻断异常
    Given 存在开放、待客户、拒收或安全封存异常
    When 提交放行
    Then 保持隔离
    And 不创建决定或成功事件

  @generated @atc-rec-006 @boundary
  Scenario: TC-REC-006-04 过期或空交集
    Given 条件已过期或允许交集为空
    When 提交放行
    Then UNKNOWN 失败关闭
    And 保持隔离

  @generated @atc-rec-006 @permission
  Scenario: TC-REC-006-05 越权放行
    Given 调用人缺少能力或对象范围
    When 提交放行
    Then 统一拒绝
    And 追加脱敏失败审计

  @generated @atc-rec-006 @concurrency
  Scenario: TC-REC-006-06 并发异常或版本变化
    Given 预览后对象或异常改变
    When 使用旧版本放行
    Then 版本冲突或锁内重新评估阻断
    And 不发布成功事件

  @generated @atc-rec-006 @recovery
  Scenario: TC-REC-006-07 原子回滚与重试
    Given 审计或 Outbox 失败
    When 提交并重试
    Then 首笔全部回滚
    And 重试只产生一个逻辑决定

  @generated @atc-rec-006 @regression
  Scenario: TC-REC-006-08 资格端口版本回归
    Given 已有 ReleaseDecision
    When 分别查询 v1 和 v2
    Then v1 仍失败关闭
    And v2 使用固定决定

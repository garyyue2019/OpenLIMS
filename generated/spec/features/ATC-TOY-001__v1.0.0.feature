# GENERATED FILE — DO NOT EDIT. openlims-specgen@0.1.0
# Source: ATC-TOY-001@1.0.0
# Spec-Fingerprint: 9378943c2fa4dd87ff918c12953d127006619a49d31a41286c1965fcd9cfc70b
Feature: ATC-TOY-001 实施 DEV-024 玩具年龄分级判定与可触及性评估
  年龄分级是玩具检测一切要求的入口——它决定适用条款、样品需求与标签审查。把客户声明和实验室判定分开存，是因为它们经常不一致，而实验室要为自己的判定负责；把判定冻结成不可变版本，是因为改判必须留下'当初是怎么判的'。可触及性同理：滥用试验之后暴露出来的部件会带出新的机械、化学与标签要求，不触发重评就等于漏检。

  @generated @atc-toy-001 @positive
  Scenario: TC-TOY-001-01 声明与判定分别留痕
    Given 产品尚无声明与判定
    When 先记录客户声明再做出实验室判定
    Then 两条事实各自可取回
    And 判定不携带声明内容
    And 审计+发件箱同事务

  @generated @atc-toy-001 @negative
  Scenario: TC-TOY-001-02 判定四要素缺一不可
    Given 产品已有声明
    When 分别缺依据、缺标准引用、缺批准人、给非法年龄
    Then 逐项 TOY.VALIDATION_FAILED
    And 不产生判定

  @generated @atc-toy-001 @positive
  Scenario: TC-TOY-001-03 AC-TOY-001 改判追加新版本
    Given V1 判定已冻结生效
    When 客户改口后追加 V2 判定并冻结
    And 分别按 V1/V2 取回
    Then V2 为唯一 EFFECTIVE，V1 转 SUPERSEDED
    And 按 V1 取回仍返回其自身依据与批准人
    And V1 内容未被改写

  @generated @atc-toy-001 @negative
  Scenario: TC-TOY-001-04 冻结后不得原地改写
    Given V1 判定已冻结
    When 再次冻结 V1，或 UPDATE/DELETE 其行
    Then TOY.DECISION_FROZEN
    And 数据库 55000 拒绝改写

  @generated @atc-toy-001 @negative
  Scenario: TC-TOY-001-05 评估阶段与滥用事件配对
    Given 产品已有 INITIAL 评估
    When AFTER_ABUSE 不给事件标识，或 INITIAL 携带事件标识，或首个评估不是 INITIAL
    Then 逐项 TOY.VALIDATION_FAILED

  @generated @atc-toy-001 @positive
  Scenario: TC-TOY-001-06 新暴露部件触发三范围重评
    Given INITIAL 评估不含内部电池仓
    When 记录含内部电池仓的 AFTER_ABUSE 评估
    Then 机械/化学/标签各一条 PENDING 触发
    And 触发携带新暴露部件清单
    And 可触及性状态为 REASSESSMENT_PENDING

  @generated @atc-toy-001 @boundary
  Scenario: TC-TOY-001-07 无新增暴露不触发
    Given INITIAL 评估已记录
    When 记录部件集合相同或更少的 AFTER_NORMAL_USE 评估
    Then 不产生任何触发
    And 可触及性状态为 SETTLED

  @generated @atc-toy-001 @negative
  Scenario: TC-TOY-001-08 结清与重复结清
    Given 存在三条 PENDING 触发
    When 逐条结清后再次结清同一条
    Then 全部结清后状态 SETTLED
    And 重复结清 TOY.REASSESSMENT_NOT_PENDING

  @generated @atc-toy-001 @negative
  Scenario: TC-TOY-001-09 追加式与并发
    Given 产品已有判定与评估
    When UPDATE/DELETE 任一事实表，及并发追加同一产品的判定
    Then 55000 拒绝
    And 恰一个成功，另一方 TOY.EXPECTED_VERSION_CONFLICT

  @generated @atc-toy-001 @negative
  Scenario: TC-TOY-001-10 平台证据失败回滚与端口固定
    Given 审计或发件箱注入失败；另有含历史判定的产品
    When 追加判定；以正确/过期版本与未知规则集查询状态端口
    Then 判定回滚为零且 audit_attempt 恰一次
    And 端口返回 ALLOWED / UNKNOWN[VERSION_MISMATCH] / UNKNOWN[RULE_SET_VERSION_UNKNOWN]

# GENERATED FILE — DO NOT EDIT. openlims-specgen@0.1.0
# Source: ATC-RPT-001@1.0.0
# Spec-Fingerprint: 86ba2b2323216eb56e7180564cbefd0e984f9947aa28b3a966e3f92ac2597b7c
Feature: ATC-RPT-001 实施 DEV-022 报告签发门禁
  报告首次成为受治理对象：每一行都能追溯到当前有效采用与完整贡献链，认可资格逐行按六维计算而非机构级布尔，签发前的每一个阻断都指名对象、规则版本与下一步——这正是 GOAL-008（超认可/超授权签发事件为零）与 RISK-019（认可简化为机构标记）的结构性防线。

  @generated @atc-rpt-001 @positive
  Scenario: TC-RPT-001-01 装配与贡献链固定
    Given 采用门禁 ALLOWED 的结果组
    When 创建报告并追加行
    Then 采用目标与组版本原样固定
    And 贡献链引用齐备
    And 审计+发件箱同事务

  @generated @atc-rpt-001 @negative
  Scenario: TC-RPT-001-02 AC-RPT-001 三类阻断逐项返回
    Given 一行收样身份冲突、一行 QC 阻断、一行签字人无资格
    When 请求门禁评估
    Then 签发被阻止
    And 三个阻断项各含对象/规则版本/原因码/下一步
    And 不聚合为单一布尔

  @generated @atc-rpt-001 @boundary
  Scenario: TC-RPT-001-03 AC-ACC-001 混合认可逐行判定
    Given 一行六维全部在范围内、另一行方法不在范围内
    And 机构级已认可标记存在
    When 查询行级认可状态
    Then 逐行独立返回 ACCREDITED / NOT_ACCREDITED
    And 非认可行使用认可声明被拒
    And 机构标记不改变判定
    And 过期/版本不匹配/缺失引用判为不在范围

  @generated @atc-rpt-001 @regression
  Scenario: TC-RPT-001-04 AC-TRACE-001 全链重建与缺失阻断
    Given 聚合自三个平行试样的采用
    When 重建贡献链并逐环节移除必需引用
    Then 完整链可重建
    And 缺任一环节阻断并指明缺失
    And 重复归属被拒

  @generated @atc-rpt-001 @negative
  Scenario: TC-RPT-001-05 端口 UNKNOWN 失败关闭
    Given 任一来源端口返回 UNKNOWN 或抛异常
    When 门禁评估
    Then 阻断且原因码指明来源
    And 报告不进入待批准
    And audit_attempt 留痕

  @generated @atc-rpt-001 @negative
  Scenario: TC-RPT-001-06 EVALUATED 分区阻断
    Given 范围分区为 EVALUATED 的行
    When 门禁评估
    Then RPT.CONFORMITY_DECISION_UNAVAILABLE
    And 不得默认放行（OD-034 未决）

  @generated @atc-rpt-001 @negative
  Scenario: TC-RPT-001-07 追加式与并发
    Given 已有报告事实
    When UPDATE/DELETE 及并发同版本提交
    Then 55000 拒绝
    And 恰一个成功，另一方 EXPECTED_VERSION_CONFLICT

  @generated @atc-rpt-001 @negative
  Scenario: TC-RPT-001-08 平台证据失败回滚
    Given 审计或发件箱注入失败
    When 创建报告
    Then 业务事实回滚为零
    And audit_attempt 恰一次

  @generated @atc-rpt-001 @boundary
  Scenario: TC-RPT-001-09 门禁端口版本固定
    Given 已评估的报告
    When 正确/过期版本与未知规则集查询
    Then ALLOWED/BLOCKED / UNKNOWN[VERSION_MISMATCH] / UNKNOWN[RULE_SET_VERSION_UNKNOWN]

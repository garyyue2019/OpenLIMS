# GENERATED FILE — DO NOT EDIT. openlims-specgen@0.1.0
# Source: ATC-TEX-004@1.0.0
# Spec-Fingerprint: 9e64c1aeb3589b0f0dbbab8f6a2a73973d994526632f44f03ae7d534b2714989
Feature: ATC-TEX-004 实施 DEV-028 纺织样品需求与 CuttingPlan 运行时
  纺织技术人员能够以精确版本输入计算并保存可解释的样品需求，看到按款色部件部位聚合的面积缺口，形成来源可追溯的 CuttingPlan；只有样品充足、规则已知且结构有效的计划才能被具备明确能力的人员批准，互斥裁样、权限、并发或证据写入失败不会产生半完成事实。

  @generated @atc-tex-004 @positive
  Scenario: TC-TEX-004-01 样品充足并批准 CuttingPlan
    Given 完整版本输入、样品面积充足、有效 CuttingPlan 和授权批准人
    When 计算需求、创建计划并批准
    Then SUFFICIENT
    And APPROVED 计划固定需求版本/哈希/规则集
    And 状态端口 ALLOWED

  @generated @atc-tex-004 @boundary
  Scenario: TC-TEX-004-02 面积不足失败关闭
    Given 三个平行加复测预留和留样，可用面积少于需求
    When 计算并尝试批准计划
    Then INSUFFICIENT 缺口按款色部件部位并列方向/项目
    And 批准拒绝
    And 补样/范围变更 Outbox 证据存在

  @generated @atc-tex-004 @negative
  Scenario: TC-TEX-004-03 互斥破坏共享拒绝
    Given 同一裁片被不同互斥破坏组或多条破坏性行共享
    When 计算样品需求
    Then TEX.EXCLUSIVE_SHARE_REJECTED
    And 无半完成需求或计划

  @generated @atc-tex-004 @negative
  Scenario: TC-TEX-004-04 UNKNOWN 失败关闭
    Given 未知规则集或未知方向
    When 计算、创建或查询状态
    Then UNKNOWN 或稳定错误码
    And 不可批准
    And 状态端口不返回 ALLOWED

  @generated @atc-tex-004 @boundary
  Scenario: TC-TEX-004-05 CuttingPlan 结构边界
    Given 尺寸非正、距布边负数或生成试样数与计划数不一致
    When 创建计划
    Then TEX.VALIDATION_FAILED
    And 无业务事实

  @generated @atc-tex-004 @permission
  Scenario: TC-TEX-004-06 批准权限
    Given 具备 manage 但不具备 textile.cutting-plan.approve 的行为人
    When 批准计划
    Then TEX.NOT_AUTHORIZED
    And 批准事实为零
    And 失败尝试留痕

  @generated @atc-tex-004 @concurrency
  Scenario: TC-TEX-004-07 并发计划版本
    Given 同一 cuttingPlanId 两个请求使用相同 expectedCurrentVersion
    When 并发追加
    Then 恰一个成功
    And 另一方 TEX.EXPECTED_VERSION_CONFLICT
    And 版本连续

  @generated @atc-tex-004 @audit
  Scenario: TC-TEX-004-08 审计或 Outbox 失败回滚
    Given 注入 audit_intent 或 outbox 写入失败
    When 创建需求、计划或批准
    Then 业务事实与同事务证据全部回滚
    And 独立失败尝试恰一条

  @generated @atc-tex-004 @recovery
  Scenario: TC-TEX-004-09 失败后安全重试
    Given 首次请求在提交前失败且 correlationId 保持不变
    When 以当前 expectedCurrentVersion 重试
    Then 至多一个业务版本
    And 无重复批准或重复 Outbox
    And 原失败证据保留

  @generated @atc-tex-004 @regression
  Scenario: TC-TEX-004-10 追加式数据库约束
    Given 已保存需求、计划和批准
    When 直接 UPDATE 或 DELETE
    Then 数据库拒绝
    And 原事实可重建且哈希不变

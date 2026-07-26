# GENERATED FILE — DO NOT EDIT. openlims-specgen@0.1.0
# Source: ATC-QC-001@1.0.0
# Spec-Fingerprint: 17119ce811f55636ddb354a0ea4ba4bfb627bafe6bbd3860f6966cf8dfdbe94f
Feature: ATC-QC-001 实施 DEV-021 QC 影响传播
  QC 从批次层的'冻结原因声明'升级为可执行的质量关口：规则按方法版本执行留下可追事实，失败影响一次性覆盖全批而非单条结果，解除阻断必须五关口齐备且偏差获批无法走捷径——这正是 RISK-006（QC 形式审批后错误放行）的结构性防线。

  @generated @atc-qc-001 @positive
  Scenario: TC-QC-001-01 方法版本驱动执行
    Given 未冻结批次与固定方法/规则集版本
    When 开启运行并逐条落 QCResult
    Then 版本原样固定
    And 全 PASS → PASSED
    And 审计+发件箱同事务

  @generated @atc-qc-001 @positive
  Scenario: TC-QC-001-02 失败与全量影响传播
    Given 一条规则 FAIL
    When 判定并登记覆盖全批的影响集
    Then 运行 FAILED
    And 全部目标登记
    And 空影响集被拒绝
    And 重复目标被拒绝

  @generated @atc-qc-001 @negative
  Scenario: TC-QC-001-03 AC-QC-001 偏差获批不解除
    Given QC 失败且已记录偏差获批
    When 影响范围与有效性决定未记录时查询可报告性并尝试解除
    Then BLOCKED 并列明未满足关口
    And 解除被拒 QC.RELEASE_GATE_INCOMPLETE
    And 无解除事实

  @generated @atc-qc-001 @boundary
  Scenario: TC-QC-001-04 五关口逐项缺失
    Given 五关口中任缺一项
    When 解除
    Then 逐项拒绝并列明缺失关口
    And 齐备后解除成功且可报告性 ALLOWED

  @generated @atc-qc-001 @negative
  Scenario: TC-QC-001-05 批次门禁失败关闭
    Given 批次状态端口 BLOCKED 或异常
    When 开启运行
    Then QC.ELIGIBILITY_BLOCKED / QC.APPLICABILITY_UNKNOWN
    And 运行事实为零
    And audit_attempt 恰一次

  @generated @atc-qc-001 @negative
  Scenario: TC-QC-001-06 追加式与并发
    Given 已有 QC 事实
    When UPDATE/DELETE 及并发同版本提交
    Then 55000 拒绝
    And 恰一个提交成功，另一方 EXPECTED_VERSION_CONFLICT

  @generated @atc-qc-001 @negative
  Scenario: TC-QC-001-07 平台证据失败回滚
    Given 审计或发件箱注入失败
    When 开启运行
    Then 业务事实回滚为零
    And audit_attempt 恰一次

  @generated @atc-qc-001 @boundary
  Scenario: TC-QC-001-08 可报告性端口版本固定
    Given 已解除与未解除运行
    When 正确/过期版本与未知规则集查询
    Then ALLOWED / BLOCKED / UNKNOWN[VERSION_MISMATCH] / UNKNOWN[RULE_SET_VERSION_UNKNOWN]

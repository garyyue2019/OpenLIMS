# GENERATED FILE — DO NOT EDIT. openlims-specgen@0.1.0
# Source: ATC-INST-001@1.0.0
# Spec-Fingerprint: a970c2e9a43bc42c3a371fe3edeb254a910a410ffba1851ec9f33c2f6673cdc8
Feature: ATC-INST-001 实施 DEV-020 首类仪器文件导入
  实验室获得首个受治理的仪器文件导入通道：原始证据不可变、解析映射可追、异常必经人工确认，为物理机械方法族的仪器数据接入提供 LAB-RAW/INT-INST 合规基础，且验证数据集比对基准（100% 一致率）从第一天固定在 CI。

  @generated @atc-inst-001 @positive
  Scenario: TC-INST-001-01 登记与不可变引用
    Given 合法文件元数据
    When 登记
    Then INGESTED
    And 哈希/仪器/解析器版本固定
    And 审计+发件箱同事务
    And 重复 SHA-256 拒绝 409

  @generated @atc-inst-001 @positive
  Scenario: TC-INST-001-02 解析行五维映射与前后值
    Given 合法行批次
    When 提交
    Then 行事实含五维映射+rawValue+parsedValue
    And 行号唯一
    And 计数落定后 COMPLETED

  @generated @atc-inst-001 @negative
  Scenario: TC-INST-001-03 异常队列失败关闭
    Given 含未知样品号与非法单位的行
    When 提交
    Then 异常行不产生行事实
    And 队列 PENDING
    And 登记 BLOCKED
    And 状态端口 BLOCKED[PENDING_EXCEPTIONS]

  @generated @atc-inst-001 @positive
  Scenario: TC-INST-001-04 人工决议保留原值
    Given PENDING 异常
    When ACCEPT_WITH_MAPPING 与 REJECT_ROW 各一
    Then 决议人/原因/时间固定
    And 原始值逐字节不变
    And 重复决议拒绝
    And 全部落定后 COMPLETED

  @generated @atc-inst-001 @regression
  Scenario: TC-INST-001-05 验证数据集 100% 一致
    Given 冻结验证数据集（含限定符/单位/异常样例）
    When 完整导入后逐字段比较
    Then 原始值、解析值、单位、限定符、样品/批次映射、异常处理一致率 100%（PRD §22-15）

  @generated @atc-inst-001 @negative
  Scenario: TC-INST-001-06 追加式与并发
    Given 已有事实
    When UPDATE/DELETE 及并发同版本提交
    Then 55000 拒绝
    And 恰一个提交成功，另一方 EXPECTED_VERSION_CONFLICT

  @generated @atc-inst-001 @negative
  Scenario: TC-INST-001-07 平台证据失败回滚
    Given 审计或发件箱注入失败
    When 登记
    Then 业务事实回滚为零
    And audit_attempt 恰一次

  @generated @atc-inst-001 @boundary
  Scenario: TC-INST-001-08 状态端口版本固定
    Given COMPLETED 登记
    When 正确/过期版本与未知规则集查询
    Then ALLOWED / UNKNOWN[VERSION_MISMATCH] / UNKNOWN[RULE_SET_VERSION_UNKNOWN]

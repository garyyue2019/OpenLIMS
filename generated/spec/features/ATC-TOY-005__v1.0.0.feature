# GENERATED FILE — DO NOT EDIT. openlims-specgen@0.1.0
# Source: ATC-TOY-005@1.0.0
# Spec-Fingerprint: 778312d5b7161d5fade4cf88c4dcc59da95bf67532bf7c137cf130337cde2556
Feature: ATC-TOY-005 修复并验收 DEV-027 Toy 结论运行时
  Toy ITEM_CONFORMITY 与 TESTED_SCOPE_CONFORMITY 结论能够在真实组织和对象范围内使用固定版本结果证据、真实录入人 SoD 与受控重认证引用安全创建；缺证据、UNKNOWN、无权限、签署绑定失败或平台证据写入失败不会产生半完成或越权结论，并恢复整个 OpenLIMS 解决方案的可构建状态。

  @generated @atc-toy-005 @positive
  Scenario: TC-TOY-005-01 ITEM 正向与真实 RecordedBy SoD
    Given ALLOWED Result adoption evidence、不同 recorder 与批准人
    When 创建 ITEM_CONFORMITY
    Then 201
    And 固定措辞
    And 事务内事实/审计/Outbox

  @generated @atc-toy-005 @positive
  Scenario: TC-TOY-005-02 TESTED_SCOPE 重认证签署绑定
    Given 多个同范围 ALLOWED evidence、完整覆盖决定、未覆盖项、重认证引用与正确内容哈希
    When 创建 TESTED_SCOPE_CONFORMITY
    Then 201
    And 签署绑定与结论不可变保存

  @generated @atc-toy-005 @negative
  Scenario: TC-TOY-005-03 结果证据 UNKNOWN 失败关闭
    Given 未知 result group/adoption/version 或端口异常
    When 创建任一结论
    Then TOY.CONCLUSION_EVIDENCE_UNKNOWN
    And 无业务事实

  @generated @atc-toy-005 @permission
  Scenario: TC-TOY-005-04 SoD 拒绝
    Given 批准人等于任一采用 target RecordedBy
    When 创建结论
    Then TOY.CONCLUSION_SOD_VIOLATION
    And 失败尝试留痕

  @generated @atc-toy-005 @permission
  Scenario: TC-TOY-005-05 对象范围与 capability 拒绝
    Given 跨组织/跨实验室或缺 capability
    When 创建或查询
    Then TOY.NOT_AUTHORIZED 或 TOY.OBJECT_NOT_ACCESSIBLE

  @generated @atc-toy-005 @negative
  Scenario: TC-TOY-005-06 签署证据缺失或哈希不匹配
    Given TESTED_SCOPE 缺精确重认证引用、intent 或正确哈希
    When 创建结论
    Then TOY.CONCLUSION_SIGNATURE_INVALID
    And 无事实

  @generated @atc-toy-005 @boundary
  Scenario: TC-TOY-005-07 覆盖决定与未覆盖项门禁
    Given 缺 coverageDecisionRef@version 或 uncoveredScopes
    When 创建 TESTED_SCOPE
    Then TOY.CONCLUSION_EVIDENCE_INCOMPLETE

  @generated @atc-toy-005 @regression
  Scenario: TC-TOY-005-08 既有政策拒绝回归
    Given 整件全面合规、自选措辞或参与判定的外部证书
    When 创建结论
    Then 保持 ATC-TOY-004 稳定拒绝码

  @generated @atc-toy-005 @audit
  Scenario: TC-TOY-005-09 审计或 Outbox 失败回滚
    Given 注入 audit_intent/outbox 失败
    When 创建结论
    Then 事实与同事务证据全部回滚
    And 独立失败尝试一条

  @generated @atc-toy-005 @recovery
  Scenario: TC-TOY-005-10 追加式数据库约束与安全重试
    Given 已创建结论或首次提交前失败
    When UPDATE/DELETE 或以同 correlationId 重试
    Then SQLSTATE 55000
    And 至多一个事实/Outbox
    And 失败证据保留

  @generated @atc-toy-005 @architecture
  Scenario: TC-TOY-005-11 Worker Toy 迁移接线
    Given Worker 模块目录
    When 应用 toy migration
    Then 发现 ToyModule
    And 旧迁移不改写
    And 新迁移单调追加

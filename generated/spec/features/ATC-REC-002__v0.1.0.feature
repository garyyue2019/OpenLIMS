# GENERATED FILE — DO NOT EDIT. openlims-specgen@0.1.0
# Source: ATC-REC-002@0.1.0
# Spec-Fingerprint: 01083113a16c88a273fa4a1d5e96a0a7b1326ff05068d28f2dacf29b35e29a4b
Feature: ATC-REC-002 生成、打印并校验包装和实物标识
  收样员能可靠区分包装和实物并通过扫码定位正确对象；标签重印不会创造新身份，也不会绕过授权或审计。

  @generated @atc-rec-002 @positive
  Scenario: TC-REC-002-01 唯一编号
    Given 同一集团的多个实验室并发登记包装和实物
    When 分配编号
    Then 集团和对象类型命名空间内所有编号唯一
    And 对象类型和必要的机构前缀可区分

  @generated @atc-rec-002 @positive
  Scenario: TC-REC-002-02 合法扫码
    Given 用户有对象权限
    And 编码有效
    When 扫码
    Then 返回正确对象、状态和允许动作

  @generated @atc-rec-002 @security
  Scenario: TC-REC-002-03 跨组织扫码
    Given 用户无编码所属法人、实验室或客户权限
    When 扫码
    Then 拒绝且不泄露对象信息
    And 记录安全事件

  @generated @atc-rec-002 @authorization
  Scenario: TC-REC-002-04 受控重印
    Given 已有标签
    And 主管有重印权限并填写原因
    When 重印
    Then 沿用同一身份
    And 新增PrintEvent
    And 审计包含原因

  @generated @atc-rec-002 @recovery
  Scenario: TC-REC-002-05 打印失败重试
    Given 打印机首次不可用
    When 以相同幂等键重试
    Then 身份不变
    And 只增加一次成功打印副作用

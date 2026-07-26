# GENERATED FILE — DO NOT EDIT. openlims-specgen@0.1.0
# Source: ATC-QTY-001@1.0.0
# Spec-Fingerprint: 29b0d4a4add02323e0db9200b320c25aa97dcd3661bfd49296afafadd97856a9
Feature: ATC-QTY-001 实施 DEV-009 不可变数量流水与并发预留
  实验室可以为每个收样对象建立可审计的不可变数量账，任何下游在预留或消耗样品量前可用公共端口验证精确账户版本的可用量；并发超分配、负余额和伪精确数量被系统性阻断。

  @generated @atc-qty-001 @positive
  Scenario: TC-QTY-001-01 建账与收货过账
    Given 对象引用完整且可计量
    And 授权有效
    When 建账并收货 100 克
    Then 创建 ACCOUNT@v1 并追加 RECEIPT
    And 余额与可用量为 100.00
    And 可用量查询 ALLOWED

  @generated @atc-qty-001 @boundary
  Scenario: TC-QTY-001-02 全部条目类型与精度边界
    Given 账户精度 2 位小数
    When 依次过账收货、产出、预留、预留释放、分配、消耗、归还、损耗、处置
    Then 每笔重算余额、活跃预留和可用量
    And 超过精度的金额被拒绝
    And COUNT 账户拒绝小数

  @generated @atc-qty-001 @negative
  Scenario: TC-QTY-001-03 负余额与维度不匹配
    Given 可用量小于请求量或维度/单位不匹配
    When 提交消耗或过账
    Then 稳定错误
    And 不追加条目也不推进版本

  @generated @atc-qty-001 @negative
  Scenario: TC-QTY-001-04 不可计量对象失败关闭
    Given 对象声明不可合理计量
    When 尝试建账
    Then QTY_NOT_QUANTIFIABLE
    And 不创建账户或伪精确数量

  @generated @atc-qty-001 @permission
  Scenario: TC-QTY-001-05 越权
    Given 缺少 capability 或对象范围
    When 建账、过账或查询
    Then 统一拒绝
    And 追加脱敏失败审计

  @generated @atc-qty-001 @concurrency
  Scenario: TC-QTY-001-06 并发超分配
    Given 可用量 100 克
    And 两个调用使用相同 expectedCurrentVersion
    When 并发分配各 80 克
    Then 最多一笔成功
    And 另一笔版本冲突或可用量不足
    And 有效分配不超过 100 克

  @generated @atc-qty-001 @recovery
  Scenario: TC-QTY-001-07 原子回滚
    Given 审计或 Outbox 失败
    When 过账并重试
    Then 首笔全部回滚
    And 重试只追加一个逻辑条目

  @generated @atc-qty-001 @regression
  Scenario: TC-QTY-001-08 不可变历史与冲销重记
    Given 存在已过账错误条目
    When 尝试改写历史并提交冲销加重记
    Then 数据库拒绝 UPDATE/DELETE
    And 冲销与重记均引用原条目
    And 旧账户版本可用量查询 UNKNOWN

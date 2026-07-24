# GENERATED FILE — DO NOT EDIT. openlims-specgen@0.1.0
# Source: ATC-REC-002@2.0.0
# Spec-Fingerprint: 89010ab6fd1a1f4e66e5e3e731bab4288d929e99644303936255d2b35c2f4e35
Feature: ATC-REC-002 生成、打印并校验包装和实物标识
  收样员可以批量打印并扫描校验包装和实物标签；每个对象身份稳定、不可复用，打印和重印不会绕过多机构权限、审计或隔离状态。

  @generated @atc-rec-002 @concurrency
  Scenario: TC-REC-002-01 登记事务原子分配唯一编号
    Given 同一集团多个实验室并发登记包装和实物
    When 提交收样登记事务
    Then 每个对象恰有一个不可变标识
    And 集团、对象类型和日期命名空间内编号唯一
    And 任一标识或审计写入失败时登记整体回滚

  @generated @atc-rec-002 @positive
  Scenario: TC-REC-002-02 批量网络打印
    Given 同实验室的包装和实物已有标识
    And 逻辑打印机启用且适配器可达
    When 请求批量打印
    Then 每个对象建立一份幂等任务
    And 生成固定模板版本TSPL2
    And 发送后状态为DISPATCHED而非VERIFIED

  @generated @atc-rec-002 @positive
  Scenario: TC-REC-002-03 扫码校验打印闭环
    Given 操作者有完整对象权限
    And 标签任务处于DISPATCHED
    When 扫码枪提交OL1载荷
    Then 返回正确包装或实物及业务编号
    And 打印任务进入VERIFIED
    And ReceivedItem仍为QUARANTINED

  @generated @atc-rec-002 @security
  Scenario: TC-REC-002-04 跨组织扫码不泄露
    Given 用户缺少编码对象的法人、实验室、客户或委托权限
    When 提交有效OL1载荷
    Then 统一返回OBJECT_NOT_ACCESSIBLE
    And 不返回对象类型或业务编号
    And 追加脱敏安全事件

  @generated @atc-rec-002 @authorization
  Scenario: TC-REC-002-05 受控重印和阈值
    Given 已有成功打印
    And 主管有重印权限并填写原因
    When 逐次请求重印
    Then 每次沿用同一身份且只新增一份
    And 前三次按普通重印权限处理
    And 超过三次需要override权限并告警

  @generated @atc-rec-002 @recovery
  Scenario: TC-REC-002-06 确定失败幂等重试
    Given 连接打印机前确定失败
    And 对象身份和PrintJob已存在
    When 以相同幂等键重试
    Then 返回同一逻辑任务
    And 不新增身份或重复任务
    And 恢复后只发送一次

  @generated @atc-rec-002 @recovery
  Scenario: TC-REC-002-07 不确定发送禁止自动重发
    Given 发送字节后连接中断且无法确认结果
    When Worker或用户尝试普通重试
    Then 任务保持UNKNOWN
    And 不再次发送
    And 只允许扫码校验或受控重印

  @generated @atc-rec-002 @permission
  Scenario: TC-REC-002-08 打印机实验室边界
    Given 对象属于实验室甲
    And 打印机绑定实验室乙
    When 请求打印
    Then 返回PRINTER_SCOPE_MISMATCH
    And 未连接打印机乙
    And 记录拒绝审计

  @generated @atc-rec-002 @boundary
  Scenario: TC-REC-002-09 编码格式和隐私边界
    Given 合法、损坏、未知版本和伪造OL1载荷
    When 解析扫码
    Then 只接受合法当前版本
    And 错误响应不泄露对象存在性
    And 载荷和日志不包含客户或产品正文

  @generated @atc-rec-002 @deployment-isolation
  Scenario: TC-REC-002-10 客户端不能选择集团
    Given 部署绑定集团甲
    And 请求字段或二维码尝试包含集团乙
    When 打印或扫码
    Then 请求失败关闭
    And 集团上下文保持集团甲
    And 不访问集团乙数据平面

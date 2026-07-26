# GENERATED FILE — DO NOT EDIT. openlims-specgen@0.1.0
# Source: ATC-PLT-002@1.0.0
# Spec-Fingerprint: 83df7240aff412e3206daa84239bf8c70f7598e80c810c4a3f31cabce984a433
Feature: ATC-PLT-002 实施 DEV-017 事务内审计和发件箱正式化与全链验证
  审计不可变从应用约定升级为数据库强制，普通管理员无法篡改或删除审计与发件箱事件；已交付六模块的跨模块协作首次获得真实端口的端到端组合证据，防止桩测试掩盖组合缺陷。

  @generated @atc-plt-002 @positive
  Scenario: TC-PLT-002-01 全链真实端口组合
    Given 专用库已应用平台与六模块迁移
    And 单一 DI 容器装配六模块真实服务与端口
    When 按 范围→数量→分配→批次→结果→计费 顺序执行命令
    Then 每步事实存在
    And platform.audit_intent 按顺序含每步动作
    And platform.outbox 含每步事件类型
    And 计费证据固定采用目标与组版本

  @generated @atc-plt-002 @negative
  Scenario: TC-PLT-002-02 中途失败关闭
    Given 链路执行至批次
    When 以过期分配版本挂载试样成员
    Then 批次成员不产生
    And batch.audit_attempt 记录失败
    And 无新增平台审计意图或发件箱事件泄漏

  @generated @atc-plt-002 @negative
  Scenario: TC-PLT-002-03 审计追加式强制
    Given 已有审计意图行
    When UPDATE 或 DELETE platform.audit_intent
    Then PostgresException 55000
    And 行内容不变

  @generated @atc-plt-002 @boundary
  Scenario: TC-PLT-002-04 发件箱仅派发更新
    Given 已有未派发事件行
    When DELETE、改 message_type、置 dispatched_at、再次置 dispatched_at
    Then DELETE 与改列拒绝 55000
    And 首次派发标记成功
    And 重复派发标记拒绝 55000

  @generated @atc-plt-002 @regression
  Scenario: TC-PLT-002-05 迁移幂等与就绪
    Given platform-0001 已应用
    When 应用 platform-0002 两次并查询就绪探针
    Then 无副作用
    And 迁移历史各登记一次
    And 就绪探针为真；缺 platform-0002 时为假

  @generated @atc-plt-002 @regression
  Scenario: TC-PLT-002-06 冒烟不可变断言
    Given 冒烟流程产生的审计与发件箱证据
    When 冒烟收尾
    Then 删除审计/发件箱被 55000 拒绝并被断言
    And 合法派发标记成功
    And 不再删除审计证据

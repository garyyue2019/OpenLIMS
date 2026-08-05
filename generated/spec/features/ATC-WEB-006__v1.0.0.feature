# GENERATED FILE — DO NOT EDIT. openlims-specgen@0.1.0
# Source: ATC-WEB-006@1.0.0
# Spec-Fingerprint: 080235ea3564b980b0f4ca720fee9dda155a3aa7c3f897aa164f49c0d00c7088
Feature: ATC-WEB-006 实施 Receiving 既有对象续办 Web 入口
  收样、身份评估、质量和 EHS 人员可在刷新、跨班次或从外部工作清单进入后，使用稳定对象引用重新打开既有 ReceivedItem 的身份、异常和放行操作，不必伪造一次新的收样登记。

  @generated @atc-web-006 @positive
  Scenario: TC-WEB-006-01 刷新后续办既有实物
    Given 稳定 receivedItemId、itemVersion、QUARANTINED
    When 打开并刷新续办深链接
    Then 身份、异常、放行面板重新出现
    And 不创建新收样

  @generated @atc-web-006 @positive
  Scenario: TC-WEB-006-02 载入既有异常
    Given 稳定 exceptionId 且有 exception.read
    When 打开续办工作区
    Then 调用既有异常 GET
    And 展示版本和决定
    And 可继续批准

  @generated @atc-web-006 @boundary
  Scenario: TC-WEB-006-03 稳定引用和版本边界
    Given 空 ID、版本 0 或未知状态
    When 尝试打开
    Then 本地阻止
    And 不发送请求

  @generated @atc-web-006 @permission
  Scenario: TC-WEB-006-04 面板能力不扩权
    Given 仅有部分 Receiving 能力
    When 打开工作区
    Then 对应操作可用
    And 其他操作禁用
    And 服务端仍为权威

  @generated @atc-web-006 @concurrency
  Scenario: TC-WEB-006-05 精确对象版本冲突
    Given 页面固定旧 itemVersion
    When 提交异常或放行
    Then 显示冲突
    And 不自动取最新版重试

  @generated @atc-web-006 @negative
  Scenario: TC-WEB-006-06 异常与实物绑定不一致
    Given exceptionId 属于其他 receivedItemId
    When 载入
    Then 不显示为当前实物异常
    And 失败关闭

  @generated @atc-web-006 @recovery
  Scenario: TC-WEB-006-07 网络失败不自动写
    Given 读取或写入失败
    When 页面保持打开
    Then 稳定输入保留
    And 无自动重复写入

  @generated @atc-web-006 @regression
  Scenario: TC-WEB-006-08 新建收样兼容
    Given 现有新建收样页面
    When 注册续办路由并复用面板
    Then 原路由和嵌入流程继续通过
    And 无重复组件实现

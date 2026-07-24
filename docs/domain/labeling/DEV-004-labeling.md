# DEV-004 包装与实物标签运行说明

## 交付范围

DEV-004 只为收样阶段的 `Container`（包装）和 `ReceivedItem`（收到实物）分配标签身份。`Receipt`、派生样品、试样、检测份、制备份、迁移液和提取液不在本任务范围内。

一个运行部署只绑定一个 `OrganizationGroup`。标签、打印机和扫码请求都不能选择或覆盖集团；集团多机构授权仍按法人、实验室、客户、委托和对象逐维校验。

## 标签身份

- 可读编号：`LABCODE-{CT|RI}-YYYYMMDD-######`。
- 序列在部署集团、对象类型和 UTC 日期命名空间内原子递增，永不回收。
- 二维码载荷：`OL1:{CT|RI}:{32位随机不透明引用}:{CRC32}`。
- 二维码不包含客户、产品、型号、法人、实验室或委托正文，也不是授权凭证。
- 模板固定为 `REC-CT-50X30@1.0.0` 和 `REC-RI-50X30@1.0.0`，尺寸 50×30mm、203dpi。
- 实物标签应贴在受控样品袋或吊牌上，不直接贴到可能影响检测的实物表面。

标签身份与 Container/ReceivedItem 在同一 Receiving 数据库事务中提交。打印失败不会删除对象、改变编号或使隔离失效。

## 打印机配置

打印机仅从服务端配置读取。API 和 Worker 必须使用相同配置版本；请求只能提交 `printerId`，不能提交主机、端口或协议。

```json
{
  "Labeling": {
    "Printers": [
      {
        "printerId": "receiving-lab-a",
        "laboratoryId": "lab-a",
        "displayName": "收样台标签机",
        "host": "label-printer-01.internal",
        "port": 9100,
        "protocol": "TSPL2",
        "configurationVersion": "1.0.0",
        "enabled": true
      }
    ]
  }
}
```

要求：

- 打印机必须兼容 TSPL/TSPL2，并通过受控网络访问 TCP 9100。
- 每台逻辑打印机只绑定一个实验室；对象实验室不一致时服务端拒绝打印。
- 禁止在客户端下发打印机地址，禁止请求失败时回退到其他实验室打印机。
- 生产 DNS、网络 ACL 和打印机固件管理属于部署运维责任；本任务不选择品牌。

## 状态及恢复

```text
REQUESTED → DISPATCHING → DISPATCHED → VERIFIED
                  ├────→ FAILED
                  └────→ UNKNOWN
```

- `DISPATCHED`：适配器已完成发送，不代表物理出纸。
- `VERIFIED`：操作者扫描该对象的合法二维码，服务端授权通过并完成打印闭环。
- `FAILED`：能够确定没有完成发送；Worker 对确定失败最多使用同一任务安全尝试三次。
- `UNKNOWN`：连接在发送阶段中断，无法判断是否已经出纸。系统禁止自动重发；操作者必须扫描疑似标签，或填写原因执行受控重印。

不得把 `DISPATCHED` 展示成“已实际打印”。打印和扫码不会改变 `ReceivedItem` 的 `QUARANTINED` 状态。

## 权限

- `receiving.label.print`：首次打印。
- `receiving.label.scan`：扫码解析和打印校验。
- `receiving.label.reprint`：填写原因后受控重印一张。
- `receiving.label.reprint.override`：同一对象累计三次成功重印后继续重印。

普通系统管理员默认没有以上权限。每次操作还必须同时具有对象所属法人、实验室、客户和服务委托访问权。

## 迁移与启动

应用正常启动不会执行数据库迁移。发布时按顺序独立执行：

```powershell
dotnet run --project src/host/worker/OpenLIMS.Worker/OpenLIMS.Worker.csproj -c Release --no-build -- --apply-module-migration receiving
dotnet run --project src/host/worker/OpenLIMS.Worker/OpenLIMS.Worker.csproj -c Release --no-build -- --apply-module-migration labeling
```

Receiving 的 `20260724_002_label_identity` 是追加迁移，不修改 `20260724_001_receipt_registration`。Labeling 使用独立 `labeling` Schema 和迁移历史。

## 验证入口

```powershell
pwsh -File scripts/verify.ps1 -Profile task -Module labeling
pwsh -File scripts/verify.ps1 -Profile architecture
pwsh -File scripts/verify.ps1 -Profile contracts
```

Linux 使用等价的 `scripts/verify.sh --profile ...`。PostgreSQL 集成测试要求 `OPENLIMS_TEST_POSTGRES_CONNECTION` 指向隔离的合成测试数据库。

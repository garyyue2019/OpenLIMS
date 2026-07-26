# DEV-013 制备/分析批最小切片

## 交付范围（OD-030 最小口径）

在用户批准的 `OD-030@1.0.0`（LIMS 最小记录+外部引用）下交付 batch 模块：

- **类型化批次**（OPS-BATCH-001）：`PREPARATION`/`PRECONDITIONING`/`ANALYTICAL`/`INSTRUMENT_RUN` 显式枚举，禁止通用 ExecutionRun；
- **跨委托成员与客户隔离**（OPS-BATCH-002）：试样成员必须先经 `AllocationStatusPort` 返回 ALLOWED（gate-then-commit，端口决定原样固定），批准 QC 样以版本化引用入批；每个成员固定自身客户/委托/产品类别归属；
- **外部证据引用**（OD-030）：CDS/ELN/仪器原始数据保留在源系统，以稳定 ID + 版本 + SHA-256 哈希追加钉入批次，LIMS 不复制内容、不做仪器控制；
- **整批冻结**（OPS-BATCH-003 / AC-BATCH-001）：QC 失败、环境超差或校准失效冻结全部成员，原批次与数据保留，冻结后拒绝新增成员与证据，批准的后续处理以引用记录——不得选择性重开有利结果。

## 权限与并发

- 唯一新增能力 `batch.manage`，对象范围为法人+实验室（批次跨客户/委托，成员级归属单独固定）；
- 批级 advisory lock + `expectedCurrentVersion`：创建 v1，成员/证据/冻结各推进 vN+1，并发提交最多一笔成功；
- 领域与数据库触发器双重禁止 UPDATE/DELETE。

## API 与公共端口

- `POST /api/v1/batches`、`POST /api/v1/batches/{id}/members`、`POST /api/v1/batches/{id}/evidence`、`POST /api/v1/batches/{id}/freeze`、`GET /api/v1/batches/{id}`、`GET /api/v1/batches/{id}/status`
- `IBatchStatusPort`（`BATCH-EXECUTION@1.0.0`）：当前版本且 ACTIVE 返回 `ALLOWED`；冻结或不存在返回 `BLOCKED`；旧版本或未知规则版本返回 `UNKNOWN`（等同阻断）。

## 迁移与验证

```powershell
dotnet run --project src/host/worker/OpenLIMS.Worker/OpenLIMS.Worker.csproj -c Release --no-build -- --apply-module-migration batch
```

PostgreSQL 集成测试使用专用数据库 `openlims_batch_test`（自动创建），需要 `OPENLIMS_TEST_POSTGRES_CONNECTION`。

# DEV-020 首类仪器导入（ATC-INST-001）

## 目标

OD-001 已决（玩具×物理机械）解锁本卡：交付 instrument 模块——仪器文件/CSV 导入能力。原文件只读登记（OD-030 口径：稳定引用+版本+SHA-256，不复制外部权威内容）、解析行样品/参数/单位/限定符映射且保留解析前后值、异常进入人工确认队列（人工决议保留原值）、重复文件哈希拒绝、追加式+DB 触发器、公开导入状态端口（ALLOWED/BLOCKED/UNKNOWN）。验证数据集契约测试逐字段比较一致率 100%（PRD §22 第 15 条）。生产仪器清单排序属 OD-031 延后范围，本卡为能力交付不做该决定。

## 阶段

1. [completed] 侦察：INT-INST-001/002、INT-DATA-001、LAB-RAW-001/002 全在基线；无既有 INST 规格；OD-030 已决供引用口径；OD-031 延后仪器清单保持 open；批次模块 batch_evidence 已有 INSTRUMENT 来源枚举供未来消费。
2. [completed] 规格 BUS-INST-001/002/003 + ATC-INST-001 并 READY；契约测试 141→145、特性 49→50。
3. [completed] contracts/instrument + src/modules/instrument（八件套）+ 宿主/slnx/verify/OpenAPI 接线。
4. [completed] 单元/契约/集成/架构测试（专用 openlims_instrument_test）；验证数据集 100% 一致契约测试。
5. [completed] 完整门禁 + 代码评审工作流，CI 全绿后按授权提交/PR/合并。

## 约束

- 模块模板：私有 schema、追加式 55000 触发器、advisory lock + expectedCurrentVersion、独立 audit_attempt、平台 audit_intent+outbox 同事务、HttpClaims 精确 claim（instrument.import 单一新能力）、状态端口 UNKNOWN=阻断。
- 不触碰 OD-031/OD-012；PRD 只读。

## 错误记录

| 错误 | 尝试 | 处理 |
|---|---:|---|
| 宿主 csproj 缺 instrument 项目引用导致 CS0234 | 1 | 补 API/Worker ProjectReference。 |
| verify 脚本 ai 行实际为 `Profile=ai` 而非 FullyQualifiedName，首次替换未命中 | 1 | 按实际文本重新插入 `'instrument' = 'Profile=instrument'`。 |
| 传递锁文件（10 个）落在 allowed_paths 外 | 1 | 依 DEV-015 先例在故事 allowed_paths 显式列出。 |
| 评审确认：异常队列行号重投撞唯一约束并错映射为版本冲突 | 1 | ClassifyRows 区分两类占用 + 裸 23505 映射修正 + 4 个回归测试。 |

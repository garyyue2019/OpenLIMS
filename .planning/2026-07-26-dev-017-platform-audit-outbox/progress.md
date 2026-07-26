# DEV-017 进度

## 2026-07-26

- DEV-016 合并后用户指示"继续"；建议清单剩余卡中 ATC-PLT-002 无 OD 阻断且发现 SEC-AUD-002 的 DB 强制缺口，选定为 DEV-017。
- 从 `main@e58a9bf` 创建分支 `codex/dev-017-platform-audit-outbox`。
- 规格 BUS-PLT-001@1.0.0 + ATC-PLT-002@1.0.0 落盘并 READY；validate=136 版本；二次 generate written=0；治理测试 17/17。
- platform-0002 迁移实现：audit_intent 追加式 + outbox 仅派发触发器（55000），IsCurrentAsync 要求双迁移；冒烟改为不可变断言+合法派发标记。
- 新增 tests/e2e/chain/OpenLIMS.Platform.ChainE2ETests：全链真实端口组合、过期分配失败关闭、平台不可变、迁移幂等/就绪共 4 用例，一次通过。
- 全仓 31 个测试项目全绿（新增 ChainE2E）。
- PR #17 CI 全绿（Specification governance + Application CI）后按授权 squash 合并为 main@eb3ab31；本地 main 已快进。main 现包含 17 个已交付切片，DEV-017 全部完成。

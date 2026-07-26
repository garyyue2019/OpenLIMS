# DEV-012 进度

## 2026-07-26

- DEV-011 合并（`main@19ab75b`）且 main CI 全绿后，按全流程授权继续。
- backlog 顺序下一批为 ATC-TEX-002/003；按用户预先批准的"纺织降级为契约切片，后续 TEX-002/003 同样处理或跳过"决定：TEX-002 跳过（CuttingPlan 契约与校验已在 DEV-011 交付并冻结，独立成卡无增量），TEX-003（OPS-TEXTILE-004 调湿/洗涤及超差）作为契约切片交付。
- 前置门禁通过：validate（109）、SOURCE CURRENT；`ATC-TEX-003@1.0.0` 尚不存在；OPS-TEXTILE-004 与 AC-TEXTILE-003 均在来源基线。
- 从 `main@4f68293` 创建分支 `codex/dev-012-textile-preconditioning`。
- 已追加 4 项批准源规格（BUS-TEX-004/005、AC-TEXTILE-003、ATC-TEX-003 均 @1.0.0，enabled_pack/DISABLED）；validate 一次通过（113 规格版本），READY，二次 generate written=0，Python 40/40。
- 已扩展 contracts/textile：TextilePreconditioningRecord（计划/实际分离 + 关联链 + 显式公差 + 批准引用）与 TextilePreconditioningRules 纯规则；新增 8 个契约测试（Textile 合计 17/17）。
- 全量门禁：strict validate、SOURCE CURRENT、HISTORY PASSED、check、written=0、locked restore、全解决方案 20 个测试项目全部通过（含 Textile 17）。
- 路径审计：23 个变更文件全部在 allowed_paths，outside_allowed=0。按授权自动提交/PR/合并。

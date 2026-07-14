# ISSUE 记录

> 说明：当前仓库可追溯到的相关提交时间为 `2026-07-14 11:00:53 +08:00`。  
> 由于现有文档均在同一提交中生成，本文件按“问题优先级”整理，并保留来源章节，后续如有新提交可继续追加。

## 2026-07-14 11:00:53 +08:00

| ID | 问题点 | 影响 | 状态 | 来源 |
|---|---|---|---|---|
| ISSUE-001 | `PLC/CAN` 当前仍是模拟实现，缺少真实硬件回归 | 真实设备链路、通讯时序、异常处理都还不能被验证 | 待验证 | `docs/集成文档.md` 第 7 节 |
| ISSUE-002 | 快捷入口与导航仍存在耦合 | `WPF` 导航层耦合度偏高，后续替换为更松耦合的消息机制时需要重构 | 待优化 | `docs/集成文档.md` 第 7 节 |
| ISSUE-003 | 厂商库反射加载未用真实 `DLL` 验证 | `VendorLibraryLoader` / `DynamicDllInvoker` 的运行时风险尚未消除 | 待验证 | `docs/集成文档.md` 第 8 节 |
| ISSUE-004 | `ControllerService` 接入后仍需确认 `ConnectionConfigJson` 解析 | 控制器配置可能出现绑定失败或参数错配 | 待确认 | `docs/集成文档.md` 第 8 节 |
| ISSUE-005 | 子流嵌套执行仍需验证 `IFlowResolver` | 递归/嵌套工作流的正确性还需要回归测试 | 待实现 | `docs/集成文档.md` 第 8 节 |

## 已修复不再记录

- `NuGet Value cannot be null`
- MaterialDesign 资源路径问题
- SQLite `nvarchar(max)` 初始化问题

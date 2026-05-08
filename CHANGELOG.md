# 更新日志

## v1.0.0 — 2026-05-09

### 功能
- 长按 R 键持续装填火枪/弩/弓（首次 0.50s 延迟后每 0.04s 递进）。
- 三级武器模式检测 + 三级弹药兼容系统，支持原版及部分模组武器。
- 装填冷却系统（火枪 2.5s / 弩 1.5s / 弓 0.8s，等级缩放 -20%/级）。
- 冷却覆盖层：物品栏槽位上显示剩余冷却倒计时数字。
- 模组武器检测到后自动禁用装填冷却。
- 跨次按键装填状态记忆（`s_loadedOnce`）。
- 装填状态兜底扫描（反射遍历 `*Type` 方法）。
- 配置文件 `EnableReloadCooldown` 手动控制开关。

### 文件
- `ComponentMusketAutoReload.cs` — 武器装填核心组件
- `MusketAutoReloadModPatches.cs` — 全部 Harmony 补丁（冷却追踪 + 覆盖层 + 三种武器发射检测）
- `MusketAutoReloadConfig.cs` — 独立配置文件系统
- `MusketAutoReloadModLoader.cs` — 模组入口
- `MusketAutoReloadDatabase.xdb` — 数据库注册

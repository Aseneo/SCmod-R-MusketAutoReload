
# 更新日志

## v1.1.0 — 2026-05-09

### 新增

- **游戏内配置界面**: 参照原版设置界面风格，主菜单右下角 "R" 按钮进入独立 Screen 页面，即时调整所有配置项，关闭即生效。
- **多语种支持**: 中文环境显示中文界面，其他语言显示英文。
- **模组武器兼容开关** (`EnableModWeaponCompat`): 开启后三级检测(类型继承/behavior绑定/反射)激活，关闭后 R 键仅对精确类型匹配的原版武器有效。
- **独立功能开关**: R键长按装填、装填冷却、模组武器兼容各自可单独开关。

### 修改

- **配置存储**: 从独立 JSON 文件迁移至 SCAPI 内建 `LoadSettings`/`SaveSettings` API，配置持久化到 `ModsSettings.xml`。
- **R键装填行为分离**: 单按 R 始终执行(不受开关影响)，长按连续装填受 `EnableLongPressReload` 控制。
- **模组武器检测**: `MarkModWeapon` 受 `EnableModWeaponCompat` 控制，关闭时完全不触发检测和冷却禁用。
- **全局方块扫描**: 新增 `ScanForModWeapons()` 在加载完成时预扫描模组武器，提前禁用冷却。
- **三级检测改良**: Level 1 中继承原版的模组子类也会标记并禁用冷却。

### 修复

- `EnableAutoReload` 关闭时单按 R 也被禁用，已分离为单按+长按独立控制。


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

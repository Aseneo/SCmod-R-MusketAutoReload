
# 更新日志

## v1.2.0 — 2026-05-09

### 修复
- **弹药消耗**: 修复使用R键装填后弹药不消耗的Bug。`ProcessInventoryItem`只更新武器槽状态不消耗弹药，需根据`pc==0`手动移除源槽位物品。
- **火枪已装填误判**: 发射后`BulletType`残留导致空枪被误判为已装填，改为`LoadState==Loaded`判断。
- **信息显示逻辑重构**: 移除不可靠的记忆(s_loadedOnce)机制，改为`m_foundAmmo`(behavior实时确认) + `s_compatibleAmmo`(弹药类型记录)双重检测。
- **长按满弹后误报**: 修复长按装满后只显示一次"已装填"之后显示"没有可用弹药"的问题。满弹后`m_reloadTimer=-FirstDelay`延迟重试，`m_foundAmmo`跨步保留。
- **单按满弹不显示已装填**: 修复单按R时不经过长按路径导致`m_didProcessThisHold`永远为false的问题。新增`HasCompatibleAmmo`扫描背包。
- **弩拉弦误报**: 修复弩拉弦成功但返回false导致误显示"没有可用的弩箭"。`TryCrankCrossbow`改为返回bool并检查是否已拉满。
- **弩拉满后无提示**: 修复弩已拉满时`TryCrankCrossbow`不检查`curDraw`直接覆盖导致的行为异常。

### 新增
- **弹药类型记录系统** (`s_compatibleAmmo`): behavior确认兼容的弹药值记录到`Dictionary<武器类型, HashSet<弹药值>>`，用于跨次按键判断背包中是否有弹药。
- **HasCompatibleAmmo**: 遍历背包检查是否含有曾记录过的兼容弹药。支持多弹药类型武器(类型1耗尽而类型2存在也能正确识别)。
- **m_foundAmmo持久化**: 从每次`TryProcessAmmo`重置改为每次新按键开始重置，使本按键期间找到过弹药的信息得以保留。
- **m_reloadTimer节流优化**: 装填中断后延迟0.5s而非直接跳-10f，避免刷屏。
- **XML文档注释**: 所有源码文件添加完整的XML文档注释。

### 修改
- **TryProcessAmmo**: 比较`ProcessInventoryItem`前后slotValue变化来判断是否真正装填成功，避免behavior返回pc=0但未消费时的误判。
- **ShowFinalStatus**: loaded判断改为四条件：`m_didProcessThisHold || IsAlreadyLoaded || m_foundAmmo || HasCompatibleAmmo`。

---

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

---

## v1.0.0 — 2026-05-09

### 功能
- 长按 R 键持续装填火枪/弩/弓（首次 0.50s 延迟后每 0.04s 递进）。
- 三级武器模式检测 + 三级弹药兼容系统，支持原版及部分模组武器。
- 装填冷却系统（火枪 2.5s / 弩 1.5s / 弓 0.8s，等级缩放 -20%/级）。
- 冷却覆盖层：物品栏槽位上显示剩余冷却倒计时数字。
- 模组武器检测到后自动禁用装填冷却。
- 配置文件 `EnableReloadCooldown` 手动控制开关。

### 文件
- `ComponentMusketAutoReload.cs` — 武器装填核心组件
- `MusketAutoReloadModPatches.cs` — 全部 Harmony 补丁（冷却追踪 + 覆盖层 + 三种武器发射检测）
- `MusketAutoReloadConfig.cs` — 独立配置文件系统
- `MusketAutoReloadModLoader.cs` — 模组入口
- `MusketAutoReloadDatabase.xdb` — 数据库注册

# MusketAutoReload - R键装填

## 项目介绍

### AI创作警告
- 本项目为 AI 创作，包含人工审查及部分修改，介意者勿用。
- 本项目仅用于学习和研究，不包含任何商业用途。
- 使用agent工具：Trae CN
- 使用模型：deepseek-v4-pro

## 模组功能

本模组MusketAutoReload为基于Survivalcraft API 1.9.0.2 创作的独立武器装填模组，从作者自己的另一个辅助向模组 VanillaEnhancement（原版辅助增强）（https://github.com/Aseneo/SCmod-VanillaEnhancementMod） 模组中独立而来，提供按R键快速装填火枪/弩/弓的功能，含冷却覆盖层显示，支持原版及部分模组武器。

## 目录结构

```
R键装填/
├── MusketAutoReload.sln                     # 解决方案文件
├── CHANGELOG.md                             # 更新日志
└── MusketAutoReload/
    ├── modinfo.json                         # 模组元数据
    ├── MusketAutoReload.csproj              # .NET 10 项目文件
    ├── MusketAutoReloadModLoader.cs         # 模组入口: 注册钩子 + Harmony 激活 + 加载配置
    ├── MusketAutoReloadModPatches.cs        # 全部 Harmony 补丁 (冷却追踪 + 冷却覆盖层 + 三种武器发射检测)
    ├── MusketAutoReloadConfig.cs            # 配置文件系统
    ├── ComponentMusketAutoReload.cs         # 武器 R 键快速装填组件 (含模组武器兼容)
    └── Assets/
        ├── MusketAutoReloadDatabase.xdb     # 数据库: 组件注册
        └── Lang/
            ├── zh-CN.json
            └── en-US.json
```

## 功能详解

### 1. R 键快速装填武器

- **文件**: [ComponentMusketAutoReload.cs](MusketAutoReload/ComponentMusketAutoReload.cs) + [MusketAutoReloadModPatches.cs](MusketAutoReload/MusketAutoReloadModPatches.cs)
- **支持武器**: 火枪、弩、弓（含原版子类 + 模组武器，通过三级检测自动适配）
- **优先使用官方 Behavior API**: 装填逻辑委托给武器的 `SubsystemBlockBehavior`，通过 `GetProcessInventoryItemCapacity` + `ProcessInventoryItem` 保证与游戏原版行为一致，并自动兼容有自定义 behavior 的模组武器
- **长按 R 键持续装填**: 长按 0.5s 后启动自动装填，之后每 0.04s 递进一步，60 发步枪约 2.9s 装满
- **弹药搜索顺序**: 从武器槽位右侧开始环绕搜索，同行靠右、同列靠上的弹药优先
- **创造模式优化**: 只搜索快捷栏（VisibleSlotsCount ≤ 10），避免扫描全物品目录
- **状态提示**: 缺失材料 `没有可用的 XX` / 已装填 `XX 已装填` / 冷却中 `装填冷却中！`
- **跨次按键状态记忆**: 满弹后反复按 R 始终正确提示"已装填"

### 2. 武器兼容性系统

#### 武器模式检测（三级，结果缓存）

| 级别 | 检测方式 | 覆盖 |
|------|---------|------|
| 1 | `block is MusketBlock/CrossbowBlock/BowBlock` | 原版 + 所有继承子类 |
| 2 | `behavior is SubsystemMusketBlockBehavior/...` | 任何注册了官方 behavior 的模组武器 |
| 3 | 反射：`GetLoadState+SetLoadState` / `GetDraw+SetDraw+GetArrowType+SetArrowType` | 沿袭原版方法签名的自定义武器 |

#### 弹药兼容（三级）

| 级别 | 检测方式 | 覆盖 |
|------|---------|------|
| 1 | `behavior.GetProcessInventoryItemCapacity(inv, weaponSlot, ammo)` — 官方 API | 标准 behavior 实现 |
| 2 | `ammoBlock is BulletBlock` / `ammoBlock is ArrowBlock` + `IsBoltType` | 原版继承链 |
| 3 | 遍历弹药块所有 `Get*Type(int)` 静态方法 | 完全自定义的模组弹药 |

#### 装填状态检测

- 优先：`GetBulletType(data) != null` / `GetArrowType(data) != null`
- 兜底：遍历武器类所有 `public static *Type(int)` 方法
- 持久记录：首次确认装填状态后跨按键保持

#### 模组武器自动禁用冷却

检测到非原版武器（通过 behavior 或反射路径匹配）时，自动禁用装填冷却以保持公平。

### 3. 装填冷却系统

| 武器 | 基础冷却 | 等级缩放 | 最低冷却 |
|------|---------|---------|---------|
| 火枪 | 2.5s | 每级 -20% | 1.0s |
| 弩 | 1.5s | 每级 -20% | 0.5s |
| 弓 | 0.8s | 每级 -20% | 0.3s |

- 通过 `MusketCooldownTracker.CooldownEnabled` 全局开关控制
- 配置项 `EnableReloadCooldown` 可手动关闭
- 检测到模组武器时自动禁用，确保公平

#### 配置文件

首次运行后自动在游戏 `Mods/` 文件夹生成 `MusketAutoReloadConfig.json`，升级时自动补齐新增字段：

| 配置项 | 默认值 | 说明 |
|--------|--------|------|
| `EnableReloadCooldown` | `true` | 启用装填冷却（检测到模组武器时自动禁用） |

### 4. 冷却覆盖层

- **文件**: [MusketAutoReloadModPatches.cs](MusketAutoReload/MusketAutoReloadModPatches.cs) `InventorySlotCooldownOverlayPatch` + `InventorySlotCooldownUpdatePatch`
- **效果**: 在物品栏每个槽位上叠加 LabelWidget，对需要装填的武器显示剩余冷却倒计时（格式：`X.X`）
- **自动隐藏**: 武器已装填或冷却结束 → 自动隐藏数字；非武器槽位 → 不显示
- **模组武器兼容**: 使用 `MusketCooldownTracker.IsReloadableWeapon()` 判断是否可装填武器，支持模组武器检测

## 架构设计

### 组件注入 (Component + xdb)

1 个自定义 Component 通过 `MusketAutoReloadDatabase.xdb` 注册到 Player 实体：

| 组件 | LoadOrder | 功能 |
|------|-----------|------|
| ComponentMusketAutoReload | 2147483646 | 武器 R 键装填 + 长按持续装填 + 冷却检测 |

### Harmony 补丁注入

全部补丁定义在 `MusketAutoReloadModPatches.cs`，由 `MusketAutoReloadModLoader.__ModInitialize()` 中的 `harmony.PatchAll()` 一次性注入：

| 补丁 | 目标方法 | 功能 |
|------|---------|------|
| InventorySlotCooldownOverlayPatch | InventorySlotWidget.ctor | 添加冷却数字 Label |
| InventorySlotCooldownUpdatePatch | InventorySlotWidget.Update | 更新冷却数字显示（含模组武器） |
| MusketFireDetectionPatch | SubsystemMusketBlockBehavior.OnAim | 火枪发射→记录冷却 |
| CrossbowFireDetectionPatch | SubsystemCrossbowBlockBehavior.OnAim | 弩发射→记录冷却 |
| BowFireDetectionPatch | SubsystemBowBlockBehavior.OnAim | 弓发射→记录冷却 |

### 配置文件系统

首次运行后自动在游戏 `Mods/` 文件夹生成 `MusketAutoReloadConfig.json`，每次启动自动适配新版字段。

- **文件**: [MusketAutoReloadConfig.cs](MusketAutoReload/MusketAutoReloadConfig.cs)
- **加载时机**: `OnLoadingFinished` 钩子中调用 `MusketAutoReloadConfig.Load()`
- **容错**: 配置文件缺失或解析失败时自动生成默认配置，不影响游戏运行
- **升级兼容**: 旧版配置文件加载后自动补齐新字段并写回
- **格式**: JSON

## 构建与安装

```powershell
dotnet build "MusketAutoReload\MusketAutoReload.csproj" -c Debug
```

输出: `MusketAutoReload\bin\Debug\MusketAutoReload.scmod`

将该文件复制到游戏 `Mods/` 目录即可。

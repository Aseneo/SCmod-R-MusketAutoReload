using System;
using System.Collections.Generic;
using System.Xml.Linq;
using Engine;
using HarmonyLib;

namespace Game {
    /// <summary>
    /// 模组主加载器，负责初始化和配置持久化
    /// </summary>
    public class MusketAutoReloadModLoader : ModLoader {
        /// <summary>
        /// 模组初始化：注册事件钩子并激活所有Harmony补丁
        /// </summary>
        public override void __ModInitialize() {
            // 注册游戏加载完成和主菜单界面创建的钩子
            ModsManager.RegisterHook("OnLoadingFinished", this);
            ModsManager.RegisterHook("OnMainMenuScreenCreated", this);
            // 创建Harmony实例并一次性注入所有补丁
            Harmony harmony = new Harmony("com.musketaoreload.test");
            harmony.PatchAll();
        }

        /// <summary>
        /// 游戏加载完成回调：预扫描模组武器 + 注册配置界面
        /// </summary>
        public override void OnLoadingFinished(List<Action> actions) {
            // 若启用模组武器兼容且冷却开启，启动时预扫描全局方块检测模组武器
            if (MusketAutoReloadConfig.EnableModWeaponCompat && MusketCooldownTracker.CooldownEnabled)
                ComponentMusketAutoReload.ScanForModWeapons();
            // 注册游戏内配置页面
            ScreensManager.AddScreen("MusketAutoReloadConfig", new MusketAutoReloadConfigScreen());
            Log.Information("MusketAutoReload Mod: Game Loaded.");
        }

        /// <summary>
        /// 主菜单创建时，在右下角底部栏添加 "R" 配置按钮
        /// </summary>
        public override void OnMainMenuScreenCreated(MainMenuScreen mainMenuScreen,
            StackPanelWidget leftBottomBar, StackPanelWidget rightBottomBar) {
            rightBottomBar.Children.Add(new BevelledButtonWidget {
                Name = "MusketAutoReloadConfigButton",
                Text = LanguageControl.Get("MusketAutoReloadConfig", 14),
                Size = new Vector2(60, 60),
                FontScale = 1.2f
            });
        }

        /// <summary>
        /// 保存配置到 ModsSettings.xml（由SCAPI自动调用）
        /// </summary>
        public override void SaveSettings(XElement xElement) {
            xElement.SetAttributeValue("EnableReloadCooldown", MusketAutoReloadConfig.EnableReloadCooldown.ToString());
            xElement.SetAttributeValue("EnableLongPressReload", MusketAutoReloadConfig.EnableLongPressReload.ToString());
            xElement.SetAttributeValue("EnableModWeaponCompat", MusketAutoReloadConfig.EnableModWeaponCompat.ToString());
        }

        /// <summary>
        /// 从 ModsSettings.xml 加载配置（由SCAPI自动调用）
        /// 兼容旧版 EnableAutoReload 字段名
        /// </summary>
        public override void LoadSettings(XElement xElement) {
            // 读取装填冷却开关
            var ec = xElement.Attribute("EnableReloadCooldown")?.Value;
            if (ec != null) MusketAutoReloadConfig.EnableReloadCooldown = bool.TryParse(ec, out bool bc) ? bc : true;
            MusketCooldownTracker.CooldownEnabled = MusketAutoReloadConfig.EnableReloadCooldown;
            // 读取长按装填开关，兼容旧版字段名 EnableAutoReload
            var el = xElement.Attribute("EnableLongPressReload")?.Value;
            if (el != null)
                MusketAutoReloadConfig.EnableLongPressReload = bool.TryParse(el, out var lr) ? lr : true;
            else
                MusketAutoReloadConfig.EnableLongPressReload = ParseBoolAttr(xElement, "EnableAutoReload", true);
            // 读取模组武器兼容开关
            MusketAutoReloadConfig.EnableModWeaponCompat = ParseBoolAttr(xElement, "EnableModWeaponCompat", true);
        }

        /// <summary>
        /// 安全解析 XML 布尔属性，缺失或解析失败时返回默认值
        /// </summary>
        static bool ParseBoolAttr(XElement el, string name, bool fallback) {
            var val = el.Attribute(name)?.Value;
            return val != null ? bool.TryParse(val, out bool b) ? b : fallback : fallback;
        }
    }
}

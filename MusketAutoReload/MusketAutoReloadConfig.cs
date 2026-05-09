using Engine;

namespace Game {
    /// <summary>
    /// R键装填全局静态配置
    /// 通过SCAPI内置的LoadSettings/SaveSettings持久化到ModsSettings.xml
    /// </summary>
    public static class MusketAutoReloadConfig {
        // 启用装填冷却（检测到模组武器时自动禁用）
        public static bool EnableReloadCooldown = true;
        // 启用R键长按连续装填（单按R始终有效，不受此开关影响）
        public static bool EnableLongPressReload = true;
        // 启用模组武器兼容三级检测
        public static bool EnableModWeaponCompat = true;
        // 是否已检测到模组武器（由MarkModWeapon设置，用于配置界面锁定冷却按钮）
        public static bool ModWeaponsDetected = false;
    }
}

using System;
using System.Collections.Generic;
using Engine;
using HarmonyLib;

namespace Game {
    // 模组主加载器，负责初始化和配置加载
    public class MusketAutoReloadModLoader : ModLoader {
        // 注册事件与Harmony补丁
        public override void __ModInitialize() {
            ModsManager.RegisterHook("OnLoadingFinished", this);
            Harmony harmony = new Harmony("com.musketaoreload.test");
            harmony.PatchAll();
        }

        // 游戏加载完成回调，加载配置并应用冷却设置
        public override void OnLoadingFinished(List<Action> actions) {
            MusketAutoReloadConfig.Load();
            MusketCooldownTracker.CooldownEnabled = MusketAutoReloadConfig.EnableReloadCooldown;
            Log.Information("MusketAutoReload Mod: Game Loaded.");
        }
    }
}

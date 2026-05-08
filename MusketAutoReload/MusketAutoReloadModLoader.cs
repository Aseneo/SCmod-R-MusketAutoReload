using System;
using System.Collections.Generic;
using Engine;
using HarmonyLib;

namespace Game {
    public class MusketAutoReloadModLoader : ModLoader {
        public override void __ModInitialize() {
            ModsManager.RegisterHook("OnLoadingFinished", this);
            Harmony harmony = new Harmony("com.musketaoreload.test");
            harmony.PatchAll();
        }

        public override void OnLoadingFinished(List<Action> actions) {
            MusketAutoReloadConfig.Load();
            MusketCooldownTracker.CooldownEnabled = MusketAutoReloadConfig.EnableReloadCooldown;
            Log.Information("MusketAutoReload Mod: Game Loaded.");
        }
    }
}

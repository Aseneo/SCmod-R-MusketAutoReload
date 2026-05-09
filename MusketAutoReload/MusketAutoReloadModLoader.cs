using System;
using System.Collections.Generic;
using System.Xml.Linq;
using Engine;
using HarmonyLib;

namespace Game {
    public class MusketAutoReloadModLoader : ModLoader {
        public override void __ModInitialize() {
            ModsManager.RegisterHook("OnLoadingFinished", this);
            ModsManager.RegisterHook("OnMainMenuScreenCreated", this);
            Harmony harmony = new Harmony("com.musketaoreload.test");
            harmony.PatchAll();
        }

        public override void OnLoadingFinished(List<Action> actions) {
            if (MusketAutoReloadConfig.EnableModWeaponCompat && MusketCooldownTracker.CooldownEnabled)
                ComponentMusketAutoReload.ScanForModWeapons();
            ScreensManager.AddScreen("MusketAutoReloadConfig", new MusketAutoReloadConfigScreen());
            Log.Information("MusketAutoReload Mod: Game Loaded.");
        }

        public override void OnMainMenuScreenCreated(MainMenuScreen mainMenuScreen,
            StackPanelWidget leftBottomBar, StackPanelWidget rightBottomBar) {
            rightBottomBar.Children.Add(new BevelledButtonWidget {
                Name = "MusketAutoReloadConfigButton",
                Text = LanguageControl.Get("MusketAutoReloadConfig", 14),
                Size = new Vector2(48, 48),
                FontScale = 1.2f
            });
        }

        public override void SaveSettings(XElement xElement) {
            xElement.SetAttributeValue("EnableReloadCooldown", MusketAutoReloadConfig.EnableReloadCooldown.ToString());
            xElement.SetAttributeValue("EnableLongPressReload", MusketAutoReloadConfig.EnableLongPressReload.ToString());
            xElement.SetAttributeValue("EnableModWeaponCompat", MusketAutoReloadConfig.EnableModWeaponCompat.ToString());
        }

        public override void LoadSettings(XElement xElement) {
            var ec = xElement.Attribute("EnableReloadCooldown")?.Value;
            if (ec != null) MusketAutoReloadConfig.EnableReloadCooldown = bool.TryParse(ec, out bool bc) ? bc : true;
            MusketCooldownTracker.CooldownEnabled = MusketAutoReloadConfig.EnableReloadCooldown;
            var el = xElement.Attribute("EnableLongPressReload")?.Value;
            if (el != null)
                MusketAutoReloadConfig.EnableLongPressReload = bool.TryParse(el, out var lr) ? lr : true;
            else
                MusketAutoReloadConfig.EnableLongPressReload = ParseBoolAttr(xElement, "EnableAutoReload", true);
            MusketAutoReloadConfig.EnableModWeaponCompat = ParseBoolAttr(xElement, "EnableModWeaponCompat", true);
        }

        static bool ParseBoolAttr(XElement el, string name, bool fallback) {
            var val = el.Attribute(name)?.Value;
            return val != null ? bool.TryParse(val, out bool b) ? b : fallback : fallback;
        }
    }
}

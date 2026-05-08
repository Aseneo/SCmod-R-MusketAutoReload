using System.Collections.Generic;
using Engine;
using Engine.Graphics;
using HarmonyLib;

namespace Game {
    public static class MusketCooldownTracker {
        public struct SlotKey {
            public IInventory Inventory;
            public int SlotIndex;

            public SlotKey(IInventory inventory, int slotIndex) { Inventory = inventory; SlotIndex = slotIndex; }

            public override bool Equals(object obj) {
                return obj is SlotKey other && ReferenceEquals(Inventory, other.Inventory) && SlotIndex == other.SlotIndex;
            }

            public override int GetHashCode() {
                int hash = Inventory != null ? Inventory.GetHashCode() : 0;
                return hash ^ SlotIndex;
            }
        }

        public static Dictionary<SlotKey, double> FireTimes = new();
        public static Dictionary<SlotKey, float> FullCooldowns = new();

        public static bool CooldownEnabled = true;

        public static void RecordFire(IInventory inventory, int slotIndex, float cooldown) {
            if (!CooldownEnabled) return;
            SlotKey key = new SlotKey(inventory, slotIndex);
            FireTimes[key] = Time.FrameStartTime;
            FullCooldowns[key] = cooldown;
        }

        public static float GetCooldownRemaining(IInventory inventory, int slotIndex) {
            if (!CooldownEnabled) return 0f;
            SlotKey key = new SlotKey(inventory, slotIndex);
            if (FireTimes.TryGetValue(key, out double fireTime)) {
                float full = GetFullCooldown(inventory, slotIndex);
                float elapsed = (float)(Time.FrameStartTime - fireTime);
                float remaining = full - elapsed;
                return remaining > 0f ? remaining : 0f;
            }
            return 0f;
        }

        public static float GetFullCooldown(IInventory inventory, int slotIndex) {
            SlotKey key = new SlotKey(inventory, slotIndex);
            if (FullCooldowns.TryGetValue(key, out float full)) { return full; }
            return 2.5f;
        }

        public static bool IsReloadableWeapon(int contents) {
            if (contents == 0 || contents >= BlocksManager.Blocks.Length) { return false; }
            if (ComponentMusketAutoReload.s_patternCache.TryGetValue(contents, out ComponentMusketAutoReload.ReloadPattern pattern)) {
                return pattern != ComponentMusketAutoReload.ReloadPattern.None;
            }
            Block block = BlocksManager.Blocks[contents];
            return block is MusketBlock || block is CrossbowBlock || block is BowBlock;
        }
    }

    [HarmonyPatch(typeof(InventorySlotWidget), MethodType.Constructor)]
    static class InventorySlotCooldownOverlayPatch {
        public static Dictionary<InventorySlotWidget, LabelWidget> Labels = new();

        static void Postfix(InventorySlotWidget __instance) {
            LabelWidget label = new LabelWidget();
            label.IsVisible = false;
            label.IsHitTestVisible = false;
            label.FontScale = 0.9f;
            label.DropShadow = true;
            label.TextAnchor = TextAnchor.Center;
            label.HorizontalAlignment = WidgetAlignment.Center;
            label.VerticalAlignment = WidgetAlignment.Center;
            __instance.Children.Add(label);
            Labels[__instance] = label;
        }
    }

    [HarmonyPatch(typeof(InventorySlotWidget), nameof(InventorySlotWidget.Update))]
    static class InventorySlotCooldownUpdatePatch {
        static void Postfix(InventorySlotWidget __instance) {
            if (__instance.m_inventory == null) { return; }
            int slotIndex = __instance.m_slotIndex;
            int slotValue = __instance.m_inventory.GetSlotValue(slotIndex);
            int contents = Terrain.ExtractContents(slotValue);
            if (!MusketCooldownTracker.IsReloadableWeapon(contents)) {
                if (InventorySlotCooldownOverlayPatch.Labels.TryGetValue(__instance, out LabelWidget lb)) { lb.IsVisible = false; }
                return;
            }
            int data = Terrain.ExtractData(slotValue);
            Block block = BlocksManager.Blocks[contents];
            bool needsReload = false;
            if (block is MusketBlock) {
                needsReload = MusketBlock.GetLoadState(data) == MusketBlock.LoadState.Empty;
            }
            else if (block is CrossbowBlock) {
                needsReload = CrossbowBlock.GetDraw(data) < 15 || !CrossbowBlock.GetArrowType(data).HasValue;
            }
            else if (block is BowBlock) {
                needsReload = !BowBlock.GetArrowType(data).HasValue;
            }
            if (!needsReload) {
                if (InventorySlotCooldownOverlayPatch.Labels.TryGetValue(__instance, out LabelWidget lb)) { lb.IsVisible = false; }
                return;
            }
            float remaining = MusketCooldownTracker.GetCooldownRemaining(__instance.m_inventory, slotIndex);
            if (InventorySlotCooldownOverlayPatch.Labels.TryGetValue(__instance, out LabelWidget label)) {
                if (remaining > 0f) {
                    label.Text = remaining.ToString("F1");
                    label.Color = new Color(255, 255, 255);
                    label.IsVisible = true;
                }
                else { label.IsVisible = false; }
            }
        }
    }

    [HarmonyPatch(typeof(SubsystemMusketBlockBehavior), nameof(SubsystemMusketBlockBehavior.OnAim))]
    static class MusketFireDetectionPatch {
        static float s_cooldown;

        static bool Prefix(SubsystemMusketBlockBehavior __instance, ComponentMiner componentMiner, AimState state) {
            if (state != AimState.Completed) { return true; }
            if (componentMiner == null || componentMiner.Inventory == null) { return true; }
            IInventory inventory = componentMiner.Inventory;
            int slotIndex = inventory.ActiveSlotIndex;
            if (slotIndex < 0) { return true; }
            int slotValue = inventory.GetSlotValue(slotIndex);
            if (Terrain.ExtractContents(slotValue) != __instance.m_MusketBlockIndex) { return true; }
            int data = Terrain.ExtractData(slotValue);
            MusketBlock.LoadState loadState = MusketBlock.GetLoadState(data);
            if (loadState == MusketBlock.LoadState.Empty) { return true; }
            if (!MusketBlock.GetHammerState(data)) { return true; }
            ComponentPlayer player = componentMiner.Entity.FindComponent<ComponentPlayer>();
            if (player != null) {
                float level = player.PlayerData.Level;
                s_cooldown = 2.5f * (1f - 0.2f * (level - 1f));
                s_cooldown = MathUtils.Max(s_cooldown, 1f);
            }
            else { s_cooldown = 2.5f; }
            return true;
        }

        static void Postfix(SubsystemMusketBlockBehavior __instance, ComponentMiner componentMiner, AimState state) {
            if (state != AimState.Completed || s_cooldown <= 0f) { s_cooldown = 0f; return; }
            if (componentMiner == null || componentMiner.Inventory == null) { s_cooldown = 0f; return; }
            IInventory inventory = componentMiner.Inventory;
            int slotIndex = inventory.ActiveSlotIndex;
            if (slotIndex < 0) { s_cooldown = 0f; return; }
            int slotValue = inventory.GetSlotValue(slotIndex);
            if (Terrain.ExtractContents(slotValue) != __instance.m_MusketBlockIndex) { s_cooldown = 0f; return; }
            int data = Terrain.ExtractData(slotValue);
            if (MusketBlock.GetLoadState(data) == MusketBlock.LoadState.Empty) {
                MusketCooldownTracker.RecordFire(inventory, slotIndex, s_cooldown);
            }
            s_cooldown = 0f;
        }
    }

    [HarmonyPatch(typeof(SubsystemCrossbowBlockBehavior), nameof(SubsystemCrossbowBlockBehavior.OnAim))]
    static class CrossbowFireDetectionPatch {
        static float s_cooldown;

        static bool Prefix(SubsystemCrossbowBlockBehavior __instance, ComponentMiner componentMiner, AimState state) {
            if (state != AimState.Completed) { return true; }
            if (componentMiner == null || componentMiner.Inventory == null) { return true; }
            IInventory inventory = componentMiner.Inventory;
            int slotIndex = inventory.ActiveSlotIndex;
            if (slotIndex < 0) { return true; }
            int slotValue = inventory.GetSlotValue(slotIndex);
            if (Terrain.ExtractContents(slotValue) != __instance.m_CrossbowBlockIndex) { return true; }
            int data = Terrain.ExtractData(slotValue);
            if (CrossbowBlock.GetDraw(data) != 15 || !CrossbowBlock.GetArrowType(data).HasValue) { return true; }
            ComponentPlayer player = componentMiner.Entity.FindComponent<ComponentPlayer>();
            if (player != null) {
                float level = player.PlayerData.Level;
                s_cooldown = 1.5f * (1f - 0.2f * (level - 1f));
                s_cooldown = MathUtils.Max(s_cooldown, 0.5f);
            }
            else { s_cooldown = 1.5f; }
            return true;
        }

        static void Postfix(SubsystemCrossbowBlockBehavior __instance, ComponentMiner componentMiner, AimState state) {
            if (state != AimState.Completed || s_cooldown <= 0f) { s_cooldown = 0f; return; }
            if (componentMiner == null || componentMiner.Inventory == null) { s_cooldown = 0f; return; }
            IInventory inventory = componentMiner.Inventory;
            int slotIndex = inventory.ActiveSlotIndex;
            if (slotIndex < 0) { s_cooldown = 0f; return; }
            MusketCooldownTracker.RecordFire(inventory, slotIndex, s_cooldown);
            s_cooldown = 0f;
        }
    }

    [HarmonyPatch(typeof(SubsystemBowBlockBehavior), nameof(SubsystemBowBlockBehavior.OnAim))]
    static class BowFireDetectionPatch {
        static float s_cooldown;

        static bool Prefix(SubsystemBowBlockBehavior __instance, ComponentMiner componentMiner, AimState state) {
            if (state != AimState.Completed) { return true; }
            if (componentMiner == null || componentMiner.Inventory == null) { return true; }
            IInventory inventory = componentMiner.Inventory;
            int slotIndex = inventory.ActiveSlotIndex;
            if (slotIndex < 0) { return true; }
            int slotValue = inventory.GetSlotValue(slotIndex);
            int contents = Terrain.ExtractContents(slotValue);
            if (contents == 0 || contents >= BlocksManager.Blocks.Length) { return true; }
            if (!(BlocksManager.Blocks[contents] is BowBlock)) { return true; }
            int data = Terrain.ExtractData(slotValue);
            if (!BowBlock.GetArrowType(data).HasValue) { return true; }
            ComponentPlayer player = componentMiner.Entity.FindComponent<ComponentPlayer>();
            if (player != null) {
                float level = player.PlayerData.Level;
                s_cooldown = 0.8f * (1f - 0.2f * (level - 1f));
                s_cooldown = MathUtils.Max(s_cooldown, 0.3f);
            }
            else { s_cooldown = 0.8f; }
            return true;
        }

        static void Postfix(SubsystemBowBlockBehavior __instance, ComponentMiner componentMiner, AimState state) {
            if (state != AimState.Completed || s_cooldown <= 0f) { s_cooldown = 0f; return; }
            if (componentMiner == null || componentMiner.Inventory == null) { s_cooldown = 0f; return; }
            IInventory inventory = componentMiner.Inventory;
            int slotIndex = inventory.ActiveSlotIndex;
            if (slotIndex < 0) { s_cooldown = 0f; return; }
            MusketCooldownTracker.RecordFire(inventory, slotIndex, s_cooldown);
            s_cooldown = 0f;
        }
    }
}

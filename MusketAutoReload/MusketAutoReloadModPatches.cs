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

        // 每个物品栏槽位的最后发射时间
        public static Dictionary<SlotKey, double> FireTimes = new();
        // 每个物品栏槽位的完整冷却时长
        public static Dictionary<SlotKey, float> FullCooldowns = new();

        // 全局冷却开关: 配置文件可手动关闭, 检测到模组武器时自动关闭
        public static bool CooldownEnabled = true;

        // 记录武器发射时间 (Harmony发射检测补丁调用)
        public static void RecordFire(IInventory inventory, int slotIndex, float cooldown) {
            if (!CooldownEnabled) return;
            SlotKey key = new SlotKey(inventory, slotIndex);
            FireTimes[key] = Time.FrameStartTime;
            FullCooldowns[key] = cooldown;
        }

        // 查询剩余冷却秒数, 已结束返回0
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

        // 判断方块是否为可装填武器 (原版或模组武器)
        public static bool IsReloadableWeapon(int contents) {
            if (contents == 0 || contents >= BlocksManager.Blocks.Length) { return false; }
            if (ComponentMusketAutoReload.s_patternCache.TryGetValue(contents, out ComponentMusketAutoReload.ReloadPattern pattern)) {
                return pattern != ComponentMusketAutoReload.ReloadPattern.None;
            }
            Block block = BlocksManager.Blocks[contents];
            return block is MusketBlock || block is CrossbowBlock || block is BowBlock;
        }
    }

    // 冷却覆盖层: 在每个 InventorySlotWidget 构造时叠加一个 LabelWidget 用于显示冷却数字
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

    // 冷却覆盖层更新: 每帧检查槽位是否需要装填, 显示 X.X 秒冷却倒计时
    [HarmonyPatch(typeof(InventorySlotWidget), nameof(InventorySlotWidget.Update))]
    static class InventorySlotCooldownUpdatePatch {
        static void Postfix(InventorySlotWidget __instance) {
            if (__instance.m_inventory == null) { return; }
            int slotIndex = __instance.m_slotIndex;
            int slotValue = __instance.m_inventory.GetSlotValue(slotIndex);
            int contents = Terrain.ExtractContents(slotValue);
            // 非可装填武器 → 隐藏冷却数字
            if (!MusketCooldownTracker.IsReloadableWeapon(contents)) {
                if (InventorySlotCooldownOverlayPatch.Labels.TryGetValue(__instance, out LabelWidget lb)) { lb.IsVisible = false; }
                return;
            }
            int data = Terrain.ExtractData(slotValue);
            Block block = BlocksManager.Blocks[contents];
            // 判断是否需要装填
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
            // 已装填 → 隐藏冷却数字
            if (!needsReload) {
                if (InventorySlotCooldownOverlayPatch.Labels.TryGetValue(__instance, out LabelWidget lb)) { lb.IsVisible = false; }
                return;
            }
            // 显示剩余冷却秒数
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

    // 火枪发射检测: 拦截 SubsystemMusketBlockBehavior.OnAim(AimState.Completed)
    // Prefix 计算发射前冷却, Postfix 在发射成功(LoadState变为Empty)后记录冷却
    [HarmonyPatch(typeof(SubsystemMusketBlockBehavior), nameof(SubsystemMusketBlockBehavior.OnAim))]
    static class MusketFireDetectionPatch {
        static float s_cooldown;

        static bool Prefix(SubsystemMusketBlockBehavior __instance, ComponentMiner componentMiner, AimState state) {
            // 仅在瞄准完成(发射)时计算冷却
            if (state != AimState.Completed) { return true; }
            if (componentMiner == null || componentMiner.Inventory == null) { return true; }
            IInventory inventory = componentMiner.Inventory;
            int slotIndex = inventory.ActiveSlotIndex;
            if (slotIndex < 0) { return true; }
            int slotValue = inventory.GetSlotValue(slotIndex);
            if (Terrain.ExtractContents(slotValue) != __instance.m_MusketBlockIndex) { return true; }
            int data = Terrain.ExtractData(slotValue);
            MusketBlock.LoadState loadState = MusketBlock.GetLoadState(data);
            // 已空或未锤击 → 不计算冷却
            if (loadState == MusketBlock.LoadState.Empty) { return true; }
            if (!MusketBlock.GetHammerState(data)) { return true; }
            // 火枪冷却: 2.5s 起, 每级 -20%, 最低 1.0s
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
            // 仅当发射成功(LoadState变为Empty)时记录冷却
            int data = Terrain.ExtractData(slotValue);
            if (MusketBlock.GetLoadState(data) == MusketBlock.LoadState.Empty) {
                MusketCooldownTracker.RecordFire(inventory, slotIndex, s_cooldown);
            }
            s_cooldown = 0f;
        }
    }

    // 弩发射检测: 弩冷却 1.5s 起, 每级 -20%, 最低 0.5s
    [HarmonyPatch(typeof(SubsystemCrossbowBlockBehavior), nameof(SubsystemCrossbowBlockBehavior.OnAim))]
    static class CrossbowFireDetectionPatch {
        static float s_cooldown;

        static bool Prefix(SubsystemCrossbowBlockBehavior __instance, ComponentMiner componentMiner, AimState state) {
            // 仅在瞄准完成(发射)时计算冷却
            if (state != AimState.Completed) { return true; }
            if (componentMiner == null || componentMiner.Inventory == null) { return true; }
            IInventory inventory = componentMiner.Inventory;
            int slotIndex = inventory.ActiveSlotIndex;
            if (slotIndex < 0) { return true; }
            int slotValue = inventory.GetSlotValue(slotIndex);
            if (Terrain.ExtractContents(slotValue) != __instance.m_CrossbowBlockIndex) { return true; }
            int data = Terrain.ExtractData(slotValue);
            // 弩弦未满或未装箭 → 不计算冷却
            if (CrossbowBlock.GetDraw(data) != 15 || !CrossbowBlock.GetArrowType(data).HasValue) { return true; }
            // 弩冷却: 1.5s 起, 每级 -20%, 最低 0.5s
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

    // 弓发射检测: 弓冷却 0.8s 起, 每级 -20%, 最低 0.3s
    [HarmonyPatch(typeof(SubsystemBowBlockBehavior), nameof(SubsystemBowBlockBehavior.OnAim))]
    static class BowFireDetectionPatch {
        static float s_cooldown;

        static bool Prefix(SubsystemBowBlockBehavior __instance, ComponentMiner componentMiner, AimState state) {
            // 仅在瞄准完成(发射)时计算冷却
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
            // 未装箭 → 不计算冷却
            if (!BowBlock.GetArrowType(data).HasValue) { return true; }
            // 弓冷却: 0.8s 起, 每级 -20%, 最低 0.3s
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

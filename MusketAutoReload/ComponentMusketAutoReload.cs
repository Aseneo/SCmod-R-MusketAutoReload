using System;
using System.Collections.Generic;
using System.Reflection;
using Engine;
using Engine.Input;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game {
    public class ComponentMusketAutoReload : Component, IUpdateable {
        public ComponentPlayer m_componentPlayer;
        public SubsystemTerrain m_subsystemTerrain;
        public SubsystemBlockBehaviors m_subsystemBlockBehaviors;

        public enum ReloadPattern { None, Musket, Crossbow, Bow }

        public static Dictionary<int, ReloadPattern> s_patternCache = new();
        public static bool s_hasModWeapons;
        public static Dictionary<int, MethodInfo> s_getLoadState = new();
        public static Dictionary<int, MethodInfo> s_getBulletType = new();
        public static Dictionary<int, MethodInfo> s_getDraw = new();
        public static Dictionary<int, MethodInfo> s_getArrowType = new();
        public static Dictionary<int, MethodInfo> s_setDraw = new();
        public static Dictionary<int, MethodInfo> s_anyTypeGetter = new();
        public static HashSet<int> s_loadedOnce = new();

        public float m_reloadTimer;
        public int m_reloadWeaponSlot = -1;
        public bool m_didProcessThisHold;
        public const float FirstDelay = 0.50f;
        public const float RepeatDelay = 0.04f;

        public static BindingFlags s_sf = BindingFlags.Public | BindingFlags.Static;

        public UpdateOrder UpdateOrder => UpdateOrder.Default;

        public override void Load(ValuesDictionary valuesDictionary, IdToEntityMap idToEntityMap) {
            m_componentPlayer = Entity.FindComponent<ComponentPlayer>(true);
            m_subsystemTerrain = Project.FindSubsystem<SubsystemTerrain>(true);
            m_subsystemBlockBehaviors = Project.FindSubsystem<SubsystemBlockBehaviors>(true);
        }

        public void Update(float dt) {
            if (m_componentPlayer == null) return;
            var miner = m_componentPlayer.ComponentMiner;
            if (miner == null) return;
            var inv = miner.Inventory;
            if (inv == null) return;
            int slot = inv.ActiveSlotIndex;
            if (slot < 0) return;
            int sv = inv.GetSlotValue(slot);
            int wc = Terrain.ExtractContents(sv);
            if (wc == 0) return;
            Block block = BlocksManager.Blocks[wc];
            var wb = m_subsystemBlockBehaviors.GetBlockBehaviors(wc);
            ReloadPattern p = DetectPattern(wc, block);
            if (p == ReloadPattern.None) { m_reloadTimer = 0f; m_reloadWeaponSlot = -1; return; }

            bool keyDown = Keyboard.IsKeyDown(Key.R);
            bool keyOnce = Keyboard.IsKeyDownOnce(Key.R);

            if (!keyDown && !keyOnce) { m_reloadTimer = 0f; m_reloadWeaponSlot = -1; return; }

            if (keyOnce || (keyDown && m_reloadWeaponSlot != slot)) {
                m_reloadTimer = 0f;
                m_reloadWeaponSlot = slot;
                m_didProcessThisHold = false;
                bool ok = ProcessSingleStep(inv, slot, wc, sv, block, wb, p);
                if (ok) { m_didProcessThisHold = true; return; }
                ShowFinalStatus(inv, slot, wc, sv, block, p);
                return;
            }
            if (keyDown) {
                m_reloadTimer += dt;
                float threshold = m_reloadTimer < FirstDelay ? FirstDelay : RepeatDelay;
                if (m_reloadTimer >= threshold) {
                    m_reloadTimer -= threshold;
                    bool ok = ProcessSingleStep(inv, slot, wc, sv, block, wb, p);
                    if (ok) { m_didProcessThisHold = true; return; }
                    ShowFinalStatus(inv, slot, wc, sv, block, p);
                    m_reloadTimer = -10f;
                }
            }
        }

        bool ProcessSingleStep(IInventory inv, int slot, int wc, int sv, Block block, SubsystemBlockBehavior[] wb, ReloadPattern p) {
            int data = Terrain.ExtractData(sv);
            float cd = MusketCooldownTracker.GetCooldownRemaining(inv, slot);
            bool ok;
            if (p == ReloadPattern.Crossbow) {
                ok = TryProcessAmmo(inv, slot, wc, data, p, wb, cd);
                if (!ok && !IsAlreadyLoaded(wc, data, block)) { TryCrankCrossbow(inv, slot, wc, data, block, cd); }
            } else {
                ok = TryProcessAmmo(inv, slot, wc, data, p, wb, cd);
            }
            if (ok) s_loadedOnce.Add(wc);
            return ok;
        }

        void ShowFinalStatus(IInventory inv, int slot, int wc, int sv, Block block, ReloadPattern p) {
            int data = Terrain.ExtractData(sv);
            bool loaded = m_didProcessThisHold || IsAlreadyLoaded(wc, data, block) || s_loadedOnce.Contains(wc);
            if (loaded) {
                ShowLoaded(block.GetDisplayName(m_subsystemTerrain, Terrain.MakeBlockValue(wc, 0, data)));
            }
            else if (p == ReloadPattern.Musket) {
                ShowMissing(GuessNextMusketAmmo(wc, data, block));
            }
            else if (p == ReloadPattern.Crossbow) {
                int draw = block is CrossbowBlock ? CrossbowBlock.GetDraw(data) : (int)s_getDraw[wc].Invoke(null, [data]);
                ShowMissing("弩箭");
            }
            else if (p == ReloadPattern.Bow) {
                ShowMissing("箭");
            }
        }

        bool TryProcessAmmo(IInventory inv, int slot, int wc, int data, ReloadPattern p, SubsystemBlockBehavior[] wb, float cd) {
            int sc = InvSearchCount(inv);
            for (int offset = 1; offset < sc; offset++) {
                int i = (slot + offset) % sc;
                int isv = inv.GetSlotValue(i);
                if (inv.GetSlotCount(i) <= 0) continue;
                foreach (var bh in wb) {
                    if (bh.GetProcessInventoryItemCapacity(inv, slot, isv) > 0) {
                        if (cd > 0f) { ShowCooldown(); return true; }
                        bh.ProcessInventoryItem(inv, slot, isv, 1, 1, out _, out _);
                        return true;
                    }
                }
            }
            return false;
        }

        void TryCrankCrossbow(IInventory inv, int slot, int wc, int data, Block block, float cd) {
            if (cd > 0f) { ShowCooldown(); return; }
            int nd = 15;
            if (block is CrossbowBlock)
                ReplaceSlot(inv, slot, wc, CrossbowBlock.SetDraw(data, nd));
            else
                ReplaceSlot(inv, slot, wc, (int)s_setDraw[wc].Invoke(null, [data, nd]));
        }

        bool IsAlreadyLoaded(int wc, int data, Block block) {
            if (block is MusketBlock) return MusketBlock.GetBulletType(data).HasValue;
            if (block is CrossbowBlock) return CrossbowBlock.GetArrowType(data).HasValue && CrossbowBlock.GetDraw(data) >= 15;
            if (block is BowBlock) return BowBlock.GetArrowType(data).HasValue;

            if (s_getBulletType.TryGetValue(wc, out var mb) && mb != null) return mb.Invoke(null, [data]) != null;
            if (s_getArrowType.TryGetValue(wc, out var ma) && ma != null) {
                object at = ma.Invoke(null, [data]);
                if (at != null) return !s_getDraw.ContainsKey(wc) || (int)s_getDraw[wc].Invoke(null, [data]) >= 15;
            }

            if (s_anyTypeGetter.TryGetValue(wc, out var mg) && mg != null) return mg.Invoke(null, [data]) != null;

            Type t = block.GetType();
            if (t == typeof(MusketBlock) || t == typeof(CrossbowBlock) || t == typeof(BowBlock)) return false;

            foreach (MethodInfo m in t.GetMethods(s_sf)) {
                if (!m.Name.EndsWith("Type")) continue;
                ParameterInfo[] pr = m.GetParameters();
                if (pr.Length == 1 && pr[0].ParameterType == typeof(int)) {
                    s_anyTypeGetter[wc] = m;
                    return m.Invoke(null, [data]) != null;
                }
            }
            s_anyTypeGetter[wc] = null;
            return false;
        }

        static string GuessNextMusketAmmo(int wc, int data, Block block) {
            if (block is MusketBlock) {
                return MusketBlock.GetLoadState(data) switch {
                    MusketBlock.LoadState.Empty => "火药",
                    MusketBlock.LoadState.Gunpowder => "棉花",
                    _ => "子弹"
                };
            }
            if (s_getLoadState.TryGetValue(wc, out var m) && m != null) {
                int ls = (int)m.Invoke(null, [data]);
                return ls == 0 ? "火药" : ls == 1 ? "棉花" : "子弹";
            }
            return "弹药";
        }

        ReloadPattern DetectPattern(int wc, Block block) {
            if (s_patternCache.TryGetValue(wc, out var c)) return c;
            if (block is MusketBlock) { EnsureMethods(block, ReloadPattern.Musket); return Cache(wc, ReloadPattern.Musket); }
            if (block is CrossbowBlock) { EnsureMethods(block, ReloadPattern.Crossbow); return Cache(wc, ReloadPattern.Crossbow); }
            if (block is BowBlock) { EnsureMethods(block, ReloadPattern.Bow); return Cache(wc, ReloadPattern.Bow); }

            var wb = m_subsystemBlockBehaviors.GetBlockBehaviors(wc);
            foreach (var bh in wb) {
                if (bh is SubsystemMusketBlockBehavior) { MarkModWeapon(); EnsureMethods(block, ReloadPattern.Musket); return Cache(wc, ReloadPattern.Musket); }
                if (bh is SubsystemCrossbowBlockBehavior) { MarkModWeapon(); EnsureMethods(block, ReloadPattern.Crossbow); return Cache(wc, ReloadPattern.Crossbow); }
                if (bh is SubsystemBowBlockBehavior) { MarkModWeapon(); EnsureMethods(block, ReloadPattern.Bow); return Cache(wc, ReloadPattern.Bow); }
            }

            Type t = block.GetType();
            if (t.GetMethod("GetLoadState", s_sf) != null && t.GetMethod("SetLoadState", s_sf) != null) { MarkModWeapon(); EnsureMethods(block, ReloadPattern.Musket); return Cache(wc, ReloadPattern.Musket); }
            if (t.GetMethod("GetDraw", s_sf) != null && t.GetMethod("SetDraw", s_sf) != null && t.GetMethod("GetArrowType", s_sf) != null && t.GetMethod("SetArrowType", s_sf) != null) { MarkModWeapon(); EnsureMethods(block, ReloadPattern.Crossbow); return Cache(wc, ReloadPattern.Crossbow); }
            if (t.GetMethod("GetArrowType", s_sf) != null && t.GetMethod("SetArrowType", s_sf) != null && t.GetMethod("GetDraw", s_sf) == null) { MarkModWeapon(); EnsureMethods(block, ReloadPattern.Bow); return Cache(wc, ReloadPattern.Bow); }
            return Cache(wc, ReloadPattern.None);
        }

        static ReloadPattern Cache(int wc, ReloadPattern p) { s_patternCache[wc] = p; return p; }

        static void MarkModWeapon() {
            if (!s_hasModWeapons) { s_hasModWeapons = true; MusketCooldownTracker.CooldownEnabled = false; }
        }

        static void EnsureMethods(Block block, ReloadPattern p) {
            Type t = block.GetType(); int wc = block.BlockIndex;
            if (p == ReloadPattern.Musket && !s_getLoadState.ContainsKey(wc)) {
                s_getLoadState[wc] = t.GetMethod("GetLoadState", s_sf);
                s_getBulletType[wc] = t.GetMethod("GetBulletType", s_sf);
            } else if (p == ReloadPattern.Crossbow && !s_getDraw.ContainsKey(wc)) {
                s_getDraw[wc] = t.GetMethod("GetDraw", s_sf);
                s_setDraw[wc] = t.GetMethod("SetDraw", s_sf);
                s_getArrowType[wc] = t.GetMethod("GetArrowType", s_sf);
            } else if (p == ReloadPattern.Bow && !s_getArrowType.ContainsKey(wc)) {
                s_getArrowType[wc] = t.GetMethod("GetArrowType", s_sf);
            }
        }

        void ShowCooldown() => m_componentPlayer.ComponentGui.DisplaySmallMessage("装填冷却中！", Color.White, true, false);
        void ShowMissing(string s) => m_componentPlayer.ComponentGui.DisplaySmallMessage(string.Format("没有可用的 {0}", s), Color.White, true, false);
        void ShowLoaded(string s) => m_componentPlayer.ComponentGui.DisplaySmallMessage(string.Format("{0} 已装填", s), Color.White, true, false);

        static void ReplaceSlot(IInventory inv, int slot, int contents, int data) {
            inv.RemoveSlotItems(slot, 1);
            int cc = Terrain.ExtractContents(inv.GetSlotValue(slot));
            if (cc == 0) cc = contents;
            inv.AddSlotItems(slot, Terrain.MakeBlockValue(cc, 0, data), 1);
        }

        static int InvSearchCount(IInventory inv) {
            if (inv is ComponentCreativeInventory) return inv.VisibleSlotsCount;
            return inv.SlotsCount;
        }
    }
}

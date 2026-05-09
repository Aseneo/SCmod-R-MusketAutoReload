using System;
using System.Collections.Generic;
using System.Reflection;
using Engine;
using Engine.Input;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game {
    /// <summary>
    /// R键武器装填核心组件，注入到Player实体
    /// 支持原版火枪/弩/弓及三级检测兼容的模组武器
    /// </summary>
    public class ComponentMusketAutoReload : Component, IUpdateable {
        public ComponentPlayer m_componentPlayer;
        public SubsystemTerrain m_subsystemTerrain;
        public SubsystemBlockBehaviors m_subsystemBlockBehaviors;

        public enum ReloadPattern { None, Musket, Crossbow, Bow }

        // 缓存每个方块类型(wc)对应的装填模式，避免每帧重复检测
        public static Dictionary<int, ReloadPattern> s_patternCache = new();
        // 是否已检测到模组武器（用于自动禁用冷却）
        public static bool s_hasModWeapons;
        // 反射缓存：模组武器的状态读取方法
        public static Dictionary<int, MethodInfo> s_getLoadState = new();
        public static Dictionary<int, MethodInfo> s_getBulletType = new();
        public static Dictionary<int, MethodInfo> s_getDraw = new();
        public static Dictionary<int, MethodInfo> s_getArrowType = new();
        public static Dictionary<int, MethodInfo> s_setDraw = new();
        // 兜底：任意 Get*Type(int) 方法缓存，用于检测模组武器是否已装填
        public static Dictionary<int, MethodInfo> s_anyTypeGetter = new();
        // 弹药类型记忆：记录behabior曾确认兼容的弹药值，用于跨次按键判断是否有弹药
        public static Dictionary<int, HashSet<int>> s_compatibleAmmo = new();

        // 长按持续装填计时器
        public float m_reloadTimer;
        // 当前正在装填的武器槽位
        public int m_reloadWeaponSlot = -1;
        // 本次按键期间是否已至少成功装填一步
        public bool m_didProcessThisHold;
        // 本次按键期间behavior是否确认背包中有兼容弹药
        public bool m_foundAmmo;
        // 长按连续装填的首次延迟与重复间隔
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

            // 松键或切武器 → 重置状态
            if (!keyDown && !keyOnce) { m_reloadTimer = 0f; m_reloadWeaponSlot = -1; return; }

            // 单按R 或 切槽后首次长按 → 执行一步装填，然后显示最终状态
            if (keyOnce || (keyDown && m_reloadWeaponSlot != slot)) {
                m_reloadTimer = 0f;
                m_reloadWeaponSlot = slot;
                m_didProcessThisHold = false;
                m_foundAmmo = false;
                bool ok = ProcessSingleStep(inv, slot, wc, sv, block, wb, p);
                if (ok) { m_didProcessThisHold = true; return; }
                ShowFinalStatus(inv, slot, wc, sv, block, p);
                return;
            }
            // 长按R且启用连续装填 → 每0.04s执行一步
            if (keyDown && MusketAutoReloadConfig.EnableLongPressReload) {
                m_reloadTimer += dt;
                float threshold = m_reloadTimer < FirstDelay ? FirstDelay : RepeatDelay;
                if (m_reloadTimer >= threshold) {
                    m_reloadTimer -= threshold;
                    bool ok = ProcessSingleStep(inv, slot, wc, sv, block, wb, p);
                    if (ok) { m_didProcessThisHold = true; return; }
                    ShowFinalStatus(inv, slot, wc, sv, block, p);
                    // 装填中断(满了/无弹药)后延迟再次尝试，避免刷屏
                    m_reloadTimer = -FirstDelay;
                }
            }
        }

        /// <summary>
        /// 执行一次装填步骤
        /// 弩特殊处理：先尝试装箭，无箭且未上弦则拉弦
        /// </summary>
        bool ProcessSingleStep(IInventory inv, int slot, int wc, int sv, Block block, SubsystemBlockBehavior[] wb, ReloadPattern p) {
            int data = Terrain.ExtractData(sv);
            float cd = MusketCooldownTracker.GetCooldownRemaining(inv, slot);
            bool ok;
            if (p == ReloadPattern.Crossbow) {
                ok = TryProcessAmmo(inv, slot, wc, data, p, wb, cd);
                // 无弹药可用且未装箭且未拉满弦 → 拉弦
                if (!ok && !m_foundAmmo && !IsAlreadyLoaded(wc, data, block)) { ok = TryCrankCrossbow(inv, slot, wc, data, block, cd); }
            } else {
                ok = TryProcessAmmo(inv, slot, wc, data, p, wb, cd);
            }
            return ok;
        }

        /// <summary>
        /// 装填失败后判断最终状态并显示提示
        /// loaded判断顺序：本次成功过 || 武器状态已满 || behavior确认有弹药 || 背包里有记录过的兼容弹药
        /// </summary>
        void ShowFinalStatus(IInventory inv, int slot, int wc, int sv, Block block, ReloadPattern p) {
            int data = Terrain.ExtractData(sv);
            bool loaded = m_didProcessThisHold || IsAlreadyLoaded(wc, data, block) || m_foundAmmo || HasCompatibleAmmo(wc, inv);
            if (loaded) {
                ShowLoaded(block.GetDisplayName(m_subsystemTerrain, Terrain.MakeBlockValue(wc, 0, data)));
            } else {
                if (p == ReloadPattern.Musket) {
                    ShowMissing(GuessNextMusketAmmo(wc, data, block));
                }
                else if (p == ReloadPattern.Crossbow) {
                    ShowMissing("弩箭");
                }
                else if (p == ReloadPattern.Bow) {
                    ShowMissing("箭");
                }
            }
        }

        /// <summary>
        /// 从武器槽位右侧环绕搜索弹药并交给behavior装填
        /// 关键逻辑：
        ///   1. behavior.GetProcessInventoryItemCapacity>0 → 弹药匹配，设m_foundAmmo并记录弹药类型
        ///   2. 比较ProcessInventoryItem前后slot value是否变化 → 真正装填成功
        ///   3. 弹药消耗由pc==0触发RemoveSlotItems
        /// </summary>
        bool TryProcessAmmo(IInventory inv, int slot, int wc, int data, ReloadPattern p, SubsystemBlockBehavior[] wb, float cd) {
            int sc = InvSearchCount(inv);
            // 从武器槽右侧开始环绕搜索，确保优先使用靠右/靠上的弹药
            for (int offset = 1; offset < sc; offset++) {
                int i = (slot + offset) % sc;
                int isv = inv.GetSlotValue(i);
                if (inv.GetSlotCount(i) <= 0) continue;
                foreach (var bh in wb) {
                    // behavior确认此弹药可装填
                    if (bh.GetProcessInventoryItemCapacity(inv, slot, isv) > 0) {
                        m_foundAmmo = true;
                        // 记录弹药类型供HasCompatibleAmmo后续判定使用
                        RecordAmmo(wc, isv);
                        if (cd > 0f) { ShowCooldown(); return false; }
                        int before = inv.GetSlotValue(slot);
                        bh.ProcessInventoryItem(inv, slot, isv, 1, 1, out int pv, out int pc);
                        // behavior返回pc==0表示弹药已消费，手动移除源槽位物品
                        if (pc == 0) { inv.RemoveSlotItems(i, 1); }
                        // 比较前后slotValue → 真实装填成功(武器状态变化)
                        if (inv.GetSlotValue(slot) != before) {
                            // 装填成功的behavior非三个原版类 → 模组自定义武器
                            if (bh is not (SubsystemMusketBlockBehavior or SubsystemCrossbowBlockBehavior or SubsystemBowBlockBehavior))
                                MarkModWeapon();
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// 记录曾经成功匹配过某武器的弹药类型(以value为键)
        /// 用于跨次按键判断背包中是否有兼容弹药
        /// </summary>
        static void RecordAmmo(int wc, int ammoValue) {
            if (!s_compatibleAmmo.TryGetValue(wc, out var set))
                s_compatibleAmmo[wc] = set = new HashSet<int>();
            set.Add(ammoValue);
        }

        /// <summary>
        /// 遍历背包检查是否含有曾经记录过的兼容弹药
        /// 弹药类型1耗尽而类型2在背包里 → 只要类型2曾被behavior确认过就会命中
        /// </summary>
        static bool HasCompatibleAmmo(int wc, IInventory inv) {
            if (!s_compatibleAmmo.TryGetValue(wc, out var set)) return false;
            int sc = InvSearchCount(inv);
            for (int i = 0; i < sc; i++) {
                if (set.Contains(inv.GetSlotValue(i)) && inv.GetSlotCount(i) > 0)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 拉弩弦到满(Draw=15)。已满则返回false不走ShowFinalStatus
        /// </summary>
        bool TryCrankCrossbow(IInventory inv, int slot, int wc, int data, Block block, float cd) {
            if (cd > 0f) { ShowCooldown(); return false; }
            int curDraw = block is CrossbowBlock ? CrossbowBlock.GetDraw(data) : (int)s_getDraw[wc].Invoke(null, [data]);
            if (curDraw >= 15) return false;
            int nd = 15;
            if (block is CrossbowBlock)
                ReplaceSlot(inv, slot, wc, CrossbowBlock.SetDraw(data, nd));
            else
                ReplaceSlot(inv, slot, wc, (int)s_setDraw[wc].Invoke(null, [data, nd]));
            return true;
        }

        /// <summary>
        /// 判断武器是否已装填完毕
        /// 原版武器通过静态API严格判断；模组武器通过反射缓存的GetBulletType/GetArrowType/anyTypeGetter判定
        /// 火枪用LoadState==Loaded而非BulletType（因发射后BulletType残留，只有LoadState归零）
        /// </summary>
        bool IsAlreadyLoaded(int wc, int data, Block block) {
            // 原版火枪：LoadState==Loaded才算已装填（避免发射后bulletType残留误判）
            if (block is MusketBlock) return MusketBlock.GetLoadState(data) == MusketBlock.LoadState.Loaded;
            // 原版弩：有箭 + 弦拉满
            if (block is CrossbowBlock) return CrossbowBlock.GetArrowType(data).HasValue && CrossbowBlock.GetDraw(data) >= 15;
            // 原版弓：有箭
            if (block is BowBlock) return BowBlock.GetArrowType(data).HasValue;

            // 模组武器：通过反射缓存的GetBulletType判断，若有GetLoadState且返回0则判定为空
            if (s_getBulletType.TryGetValue(wc, out var mb) && mb != null) {
                if (s_getLoadState.TryGetValue(wc, out var mls) && mls != null && (int)mls.Invoke(null, [data]) == 0)
                    return false;
                return mb.Invoke(null, [data]) != null;
            }
            // 模组武器：通过反射缓存的GetArrowType(+GetDraw)判断
            if (s_getArrowType.TryGetValue(wc, out var ma) && ma != null) {
                object at = ma.Invoke(null, [data]);
                if (at != null) return !s_getDraw.ContainsKey(wc) || (int)s_getDraw[wc].Invoke(null, [data]) >= 15;
            }

            // 兜底：anyTypeGetter缓存的方法
            if (s_anyTypeGetter.TryGetValue(wc, out var mg) && mg != null) return mg.Invoke(null, [data]) != null;

            return false;
        }

        /// <summary>
        /// 根据火枪LoadState推测下一步需要的装填材料
        /// </summary>
        static string GuessNextMusketAmmo(int wc, int data, Block block) {
            if (block is MusketBlock) {
                return MusketBlock.GetLoadState(data) switch {
                    MusketBlock.LoadState.Empty => "火药",
                    MusketBlock.LoadState.Gunpowder => "棉花",
                    _ => "子弹"
                };
            }
            return "弹药";
        }

        /// <summary>
        /// 三级武器模式检测，结果缓存到s_patternCache
        /// L1: 继承链(is MusketBlock/CrossbowBlock/BowBlock)
        /// L2: 官方Beahvior绑定(is SubsystemXxxBlockBehavior)
        /// L3: 反射方法签名(GetLoadState+SetLoadState / GetDraw+SetDraw+GetArrowType+SetArrowType)
        /// 受EnableModWeaponCompat开关控制，关闭时仅精确匹配原版类型
        /// </summary>
        ReloadPattern DetectPattern(int wc, Block block) {
            if (s_patternCache.TryGetValue(wc, out var c)) return c;

            if (MusketAutoReloadConfig.EnableModWeaponCompat) {
                // L1: 继承原版的所有子类，检测到非精确类型时标记为模组武器
                if (block is MusketBlock) { if (block.GetType() != typeof(MusketBlock)) MarkModWeapon(); EnsureMethods(block, ReloadPattern.Musket); return Cache(wc, ReloadPattern.Musket); }
                if (block is CrossbowBlock) { if (block.GetType() != typeof(CrossbowBlock)) MarkModWeapon(); EnsureMethods(block, ReloadPattern.Crossbow); return Cache(wc, ReloadPattern.Crossbow); }
                if (block is BowBlock) { if (block.GetType() != typeof(BowBlock)) MarkModWeapon(); EnsureMethods(block, ReloadPattern.Bow); return Cache(wc, ReloadPattern.Bow); }

                // L2: behavior绑定检测
                var wb = m_subsystemBlockBehaviors.GetBlockBehaviors(wc);
                foreach (var bh in wb) {
                    if (bh is SubsystemMusketBlockBehavior) { MarkModWeapon(); EnsureMethods(block, ReloadPattern.Musket); return Cache(wc, ReloadPattern.Musket); }
                    if (bh is SubsystemCrossbowBlockBehavior) { MarkModWeapon(); EnsureMethods(block, ReloadPattern.Crossbow); return Cache(wc, ReloadPattern.Crossbow); }
                    if (bh is SubsystemBowBlockBehavior) { MarkModWeapon(); EnsureMethods(block, ReloadPattern.Bow); return Cache(wc, ReloadPattern.Bow); }
                }

                // L3: 反射方法签名检测
                Type t = block.GetType();
                if (t.GetMethod("GetLoadState", s_sf) != null && t.GetMethod("SetLoadState", s_sf) != null) { MarkModWeapon(); EnsureMethods(block, ReloadPattern.Musket); return Cache(wc, ReloadPattern.Musket); }
                if (t.GetMethod("GetDraw", s_sf) != null && t.GetMethod("SetDraw", s_sf) != null && t.GetMethod("GetArrowType", s_sf) != null && t.GetMethod("SetArrowType", s_sf) != null) { MarkModWeapon(); EnsureMethods(block, ReloadPattern.Crossbow); return Cache(wc, ReloadPattern.Crossbow); }
                if (t.GetMethod("GetArrowType", s_sf) != null && t.GetMethod("SetArrowType", s_sf) != null && t.GetMethod("GetDraw", s_sf) == null) { MarkModWeapon(); EnsureMethods(block, ReloadPattern.Bow); return Cache(wc, ReloadPattern.Bow); }
            }
            else {
                // 纯原版模式：仅精确类型匹配
                if (block.GetType() == typeof(MusketBlock)) { EnsureMethods(block, ReloadPattern.Musket); return Cache(wc, ReloadPattern.Musket); }
                if (block.GetType() == typeof(CrossbowBlock)) { EnsureMethods(block, ReloadPattern.Crossbow); return Cache(wc, ReloadPattern.Crossbow); }
                if (block.GetType() == typeof(BowBlock)) { EnsureMethods(block, ReloadPattern.Bow); return Cache(wc, ReloadPattern.Bow); }
            }
            return Cache(wc, ReloadPattern.None);
        }

        /// <summary>
        /// 游戏加载完成时扫描全局方块列表
        /// 发现继承原版但非精确类型的武器 → 预禁用冷却
        /// </summary>
        public static void ScanForModWeapons() {
            if (s_hasModWeapons) return;
            foreach (Block block in BlocksManager.Blocks) {
                if (block == null) continue;
                Type t = block.GetType();
                if (t == typeof(MusketBlock) || t == typeof(CrossbowBlock) || t == typeof(BowBlock)) continue;
                if (block is MusketBlock || block is CrossbowBlock || block is BowBlock) {
                    MarkModWeapon();
                    return;
                }
            }
        }

        static ReloadPattern Cache(int wc, ReloadPattern p) { s_patternCache[wc] = p; return p; }

        /// <summary>
        /// 标记检测到模组武器：全局禁用装填冷却 + 锁定配置按钮
        /// </summary>
        static void MarkModWeapon() {
            if (!MusketAutoReloadConfig.EnableModWeaponCompat) return;
            if (!s_hasModWeapons) {
                s_hasModWeapons = true;
                MusketAutoReloadConfig.ModWeaponsDetected = true;
                MusketCooldownTracker.CooldownEnabled = false;
                MusketAutoReloadConfig.EnableReloadCooldown = false;
            }
        }

        /// <summary>
        /// 缓存模组武器块类的反射方法引用，避免每帧反射开销
        /// </summary>
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

        // 状态提示方法
        void ShowCooldown() => m_componentPlayer.ComponentGui.DisplaySmallMessage("装填冷却中！", Color.White, true, false);
        void ShowMissing(string s) => m_componentPlayer.ComponentGui.DisplaySmallMessage(string.Format("没有可用的 {0}", s), Color.White, true, false);
        void ShowLoaded(string s) => m_componentPlayer.ComponentGui.DisplaySmallMessage(string.Format("{0} 已装填", s), Color.White, true, false);

        /// <summary>
        /// 替换武器槽位的物品(保持contents，修改变data)
        /// </summary>
        static void ReplaceSlot(IInventory inv, int slot, int contents, int data) {
            inv.RemoveSlotItems(slot, 1);
            int cc = Terrain.ExtractContents(inv.GetSlotValue(slot));
            if (cc == 0) cc = contents;
            inv.AddSlotItems(slot, Terrain.MakeBlockValue(cc, 0, data), 1);
        }

        /// <summary>
        /// 获取可搜索的物品栏槽位数(创造模式仅搜索快捷栏)
        /// </summary>
        static int InvSearchCount(IInventory inv) {
            if (inv is ComponentCreativeInventory) return inv.VisibleSlotsCount;
            return inv.SlotsCount;
        }
    }
}

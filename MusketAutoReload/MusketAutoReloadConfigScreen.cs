using System;
using System.Xml.Linq;
using Engine;
using Engine.Graphics;
using Engine.Input;

namespace Game {
    /// <summary>
    /// R键装填游戏内配置界面
    /// 原版风格Screen，主菜单右下角"R"按钮进入
    /// 配置变更即时生效，关闭页面即可
    /// </summary>
    public class MusketAutoReloadConfigScreen : Screen {
        const string N = "MusketAutoReloadConfig";
        static string T(int k) => LanguageControl.Get(N, k);
        static string On => T(10);
        static string Off => T(11);

        public StackPanelWidget m_contentStack;
        public BevelledButtonWidget m_longReloadBtn, m_cooldownBtn, m_modCompatBtn;
        public bool m_longReload, m_cooldown, m_modCompat;
        // 模组武器检测到时锁定冷却按钮
        public bool m_cooldownLocked;

        public MusketAutoReloadConfigScreen() {
            var node = ContentManager.Get<XElement>("Screens/MusketAutoReloadConfigScreen");
            LoadContents(this, node);
            m_contentStack = Children.Find<StackPanelWidget>("ContentStack");
        }

        /// <summary>
        /// 进入页面：从静态配置读取当前值，重建UI
        /// </summary>
        public override void Enter(object[] parameters) {
            m_longReload = MusketAutoReloadConfig.EnableLongPressReload;
            m_cooldown = MusketAutoReloadConfig.EnableReloadCooldown;
            m_modCompat = MusketAutoReloadConfig.EnableModWeaponCompat;
            m_cooldownLocked = MusketAutoReloadConfig.ModWeaponsDetected && m_modCompat;

            m_contentStack.Children.Clear();
            AddHeadline(T(1));       // 武器装填
            m_longReloadBtn = AddToggleRow(T(4), m_longReload);
            m_cooldownBtn = AddToggleRow(T(5), m_cooldown);
            if (m_cooldownLocked) LockButton(m_cooldownBtn, T(12));
            AddHeadline(T(2));       // 模组兼容
            m_modCompatBtn = AddToggleRow(T(6), m_modCompat);
        }

        /// <summary>
        /// 每帧轮询按钮点击 + Esc/返回键退出
        /// </summary>
        public override void Update() {
            if (Input.Back || Input.IsKeyDownOnce(Key.Escape)
                || Children.Find<ButtonWidget>("TopBar.Back").IsClicked) {
                ScreensManager.GoBack();
                return;
            }
            if (m_longReloadBtn != null && m_longReloadBtn.IsClicked) { m_longReload = !m_longReload; m_longReloadBtn.Text = m_longReload ? On : Off; }
            if (m_cooldownBtn != null && !m_cooldownLocked && m_cooldownBtn.IsClicked) { m_cooldown = !m_cooldown; m_cooldownBtn.Text = m_cooldown ? On : Off; }
            if (m_modCompatBtn != null && m_modCompatBtn.IsClicked) { m_modCompat = !m_modCompat; m_modCompatBtn.Text = m_modCompat ? On : Off; }
        }

        /// <summary>
        /// 退出页面：一次性写回所有配置 + 清除武器模式缓存
        /// </summary>
        public override void Leave() {
            MusketAutoReloadConfig.EnableLongPressReload = m_longReload;
            MusketAutoReloadConfig.EnableReloadCooldown = m_cooldown;
            MusketAutoReloadConfig.EnableModWeaponCompat = m_modCompat;
            MusketCooldownTracker.CooldownEnabled = m_cooldown;
            // 兼容开关变更后清除缓存，强制下次R键重新检测
            ComponentMusketAutoReload.s_patternCache.Clear();
        }

        /// <summary>
        /// 分区标题：灰色大字
        /// </summary>
        void AddHeadline(string s) => m_contentStack.Children.Add(new LabelWidget {
            Text = s, FontScale = 1f, Color = Color.LightGray, MarginTop = 8, MarginBottom = 2
        });

        /// <summary>
        /// 开关行：标签 + 开启/关闭按钮
        /// </summary>
        BevelledButtonWidget AddToggleRow(string label, bool cur) {
            var p = new UniformSpacingPanelWidget { Direction = LayoutDirection.Horizontal, Margin = new Vector2(0, 3) };
            p.Children.Add(new LabelWidget { Text = label, HorizontalAlignment = WidgetAlignment.Far, VerticalAlignment = WidgetAlignment.Center, Margin = new Vector2(20, 0) });
            var b = new BevelledButtonWidget { Text = cur ? On : Off, Style = ContentManager.Get<XElement>("Styles/ButtonStyle_310x60"), VerticalAlignment = WidgetAlignment.Center, Margin = new Vector2(20, 0) };
            p.Children.Add(b);
            m_contentStack.Children.Add(p);
            return b;
        }

        /// <summary>
        /// 锁定按钮：灰色 + 禁用交互 + 显示提示文字
        /// </summary>
        void LockButton(BevelledButtonWidget btn, string text) {
            btn.IsEnabled = false;
            btn.Text = text;
            btn.Color = new Color(128, 128, 128);
        }
    }
}

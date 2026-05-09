using System;
using System.Xml.Linq;
using Engine;
using Engine.Graphics;
using Engine.Input;

namespace Game {
    public class MusketAutoReloadConfigScreen : Screen {
        const string N = "MusketAutoReloadConfig";
        static string T(int k) => LanguageControl.Get(N, k);
        static string On => T(10);
        static string Off => T(11);

        public StackPanelWidget m_contentStack;
        public BevelledButtonWidget m_longReloadBtn, m_cooldownBtn, m_modCompatBtn;
        public bool m_longReload, m_cooldown, m_modCompat;
        public bool m_cooldownLocked;

        public MusketAutoReloadConfigScreen() {
            var node = ContentManager.Get<XElement>("Screens/MusketAutoReloadConfigScreen");
            LoadContents(this, node);
            m_contentStack = Children.Find<StackPanelWidget>("ContentStack");
        }

        public override void Enter(object[] parameters) {
            m_longReload = MusketAutoReloadConfig.EnableLongPressReload;
            m_cooldown = MusketAutoReloadConfig.EnableReloadCooldown;
            m_modCompat = MusketAutoReloadConfig.EnableModWeaponCompat;
            m_cooldownLocked = MusketAutoReloadConfig.ModWeaponsDetected && m_modCompat;

            m_contentStack.Children.Clear();
            AddHeadline(T(1));
            m_longReloadBtn = AddToggleRow(T(4), m_longReload);
            m_cooldownBtn = AddToggleRow(T(5), m_cooldown);
            if (m_cooldownLocked) LockButton(m_cooldownBtn, T(12));
            AddHeadline(T(2));
            m_modCompatBtn = AddToggleRow(T(6), m_modCompat);
        }

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

        public override void Leave() {
            MusketAutoReloadConfig.EnableLongPressReload = m_longReload;
            MusketAutoReloadConfig.EnableReloadCooldown = m_cooldown;
            MusketAutoReloadConfig.EnableModWeaponCompat = m_modCompat;
            MusketCooldownTracker.CooldownEnabled = m_cooldown;
            ComponentMusketAutoReload.s_patternCache.Clear();
        }

        void AddHeadline(string s) => m_contentStack.Children.Add(new LabelWidget {
            Text = s, FontScale = 1f, Color = Color.LightGray, MarginTop = 8, MarginBottom = 2
        });

        BevelledButtonWidget AddToggleRow(string label, bool cur) {
            var p = new UniformSpacingPanelWidget { Direction = LayoutDirection.Horizontal, Margin = new Vector2(0, 3) };
            p.Children.Add(new LabelWidget { Text = label, HorizontalAlignment = WidgetAlignment.Far, VerticalAlignment = WidgetAlignment.Center, Margin = new Vector2(20, 0) });
            var b = new BevelledButtonWidget { Text = cur ? On : Off, Style = ContentManager.Get<XElement>("Styles/ButtonStyle_310x60"), VerticalAlignment = WidgetAlignment.Center, Margin = new Vector2(20, 0) };
            p.Children.Add(b);
            m_contentStack.Children.Add(p);
            return b;
        }

        void LockButton(BevelledButtonWidget btn, string text) {
            btn.IsEnabled = false;
            btn.Text = text;
            btn.Color = new Color(128, 128, 128);
        }
    }
}

// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    Theme.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-12
// Purpose: Follows the Windows theme: light or dark mode of the applications
//          (Settings > Personalization > Colors) and the accent color. Theme.Apply
//          is called by each window after Localization.Apply: dark colors for the
//          window and its controls, dark title bar and scroll bars, accent color
//          for the selection in menus and tool bars. In light mode the system
//          colors are kept; only the menus use the accent color.
// -----------------------------------------------------------------------------
using System;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace LiteDeploy
{
    static public class Theme
    {
        public const String Automatic = "auto";
        public const String Light = "light";
        public const String Dark = "dark";

        static bool? dark;
        static Color? accent;

        /// <summary>"auto" follows Windows; "light" or "dark" force the mode (UserSettings.Theme, LITEDEPLOY_THEME).</summary>
        static public String Preference = Automatic;

        static public bool IsDark
        {
            get
            {
                if (!dark.HasValue)
                {
                    String forced = Environment.GetEnvironmentVariable("LITEDEPLOY_THEME") ?? Preference;
                    if (forced == Dark) dark = true;
                    else if (forced == Light) dark = false;
                    else dark = ReadDword(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme") == 0;
                }
                return dark.Value;
            }
        }

        /// <summary>Accent color of Windows (DWM), else the selection color of the system.</summary>
        static public Color Accent
        {
            get
            {
                if (!accent.HasValue)
                {
                    int? abgr = ReadDword(@"Software\Microsoft\Windows\DWM", "AccentColor");
                    accent = abgr.HasValue
                        ? Color.FromArgb(255, abgr.Value & 0xFF, (abgr.Value >> 8) & 0xFF, (abgr.Value >> 16) & 0xFF)
                        : SystemColors.Highlight;
                }
                return accent.Value;
            }
        }

        // Dark palette (close to the Windows 11 dark applications)
        static public Color Window { get { return IsDark ? Color.FromArgb(32, 32, 32) : SystemColors.Control; } }
        static public Color Surface { get { return IsDark ? Color.FromArgb(43, 43, 43) : SystemColors.Window; } }
        static public Color Text { get { return IsDark ? Color.FromArgb(240, 240, 240) : SystemColors.ControlText; } }
        static public Color Border { get { return IsDark ? Color.FromArgb(70, 70, 70) : SystemColors.ControlDark; } }
        static public Color WarningColor { get { return IsDark ? Color.FromArgb(255, 185, 80) : Color.DarkOrange; } }
        static public Color ErrorColor { get { return IsDark ? Color.FromArgb(255, 110, 100) : Color.Firebrick; } }

        /// <summary>Forgets the colors read from Windows (after a change of the settings).</summary>
        static public void Reset()
        {
            dark = null;
            accent = null;
        }

        static public void Apply(Form form)
        {
            if (form == null) return;
            if (IsDark)
            {
                form.BackColor = Window;
                form.ForeColor = Text;
                if (form.IsHandleCreated) DarkTitleBar(form);
                else form.HandleCreated += (s, e) => DarkTitleBar(form);
                foreach (Control c in form.Controls) ApplyTo(c);
            }

            // Tool bars and menus, including context menus that are not in Controls
            foreach (FieldInfo field in form.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
            {
                ToolStrip strip = field.GetValue(form) as ToolStrip;
                if (strip != null) ApplyTo(strip);
            }
        }

        static void ApplyTo(Control c)
        {
            ToolStrip strip = c as ToolStrip;
            if (strip != null)
            {
                strip.Renderer = new ToolStripProfessionalRenderer(new Colors()) { RoundedEdges = false };
                if (IsDark)
                {
                    strip.BackColor = Window;
                    strip.ForeColor = Text;
                    foreach (ToolStripItem item in strip.Items) ApplyTo(item);
                }
                return;
            }
            if (!IsDark) return;

            if (c is TextBoxBase || c is ListBox || c is ComboBox || c is ListView || c is TreeView || c is NumericUpDown)
            {
                c.BackColor = Surface;
                c.ForeColor = Text;
                ComboBox combo = c as ComboBox;
                if (combo != null) combo.FlatStyle = FlatStyle.Flat;
                if (c is ListView || c is TreeView || c is TextBoxBase || c is ListBox)
                {
                    if (c.IsHandleCreated) DarkScrollBars(c);
                    else c.HandleCreated += (s, e) => DarkScrollBars((Control)s);
                }
            }
            else if (c is DataGridView)
            {
                DataGridView grid = (DataGridView)c;
                grid.BackgroundColor = Window;
                grid.GridColor = Border;
                grid.DefaultCellStyle.BackColor = Surface;
                grid.DefaultCellStyle.ForeColor = Text;
                grid.DefaultCellStyle.SelectionBackColor = Accent;
                grid.ColumnHeadersDefaultCellStyle.BackColor = Window;
                grid.ColumnHeadersDefaultCellStyle.ForeColor = Text;
                grid.RowHeadersDefaultCellStyle.BackColor = Window;
                grid.EnableHeadersVisualStyles = false;
            }
            else if (c is ButtonBase && !(c is CheckBox) && !(c is RadioButton))
            {
                ButtonBase b = (ButtonBase)c;
                b.FlatStyle = FlatStyle.Flat;
                b.FlatAppearance.BorderColor = Border;
                b.BackColor = Color.FromArgb(55, 55, 55);
                b.ForeColor = Text;
            }
            else
            {
                c.BackColor = c is GroupBox || c is Panel || c is SplitContainer || c is TabPage || c is UserControl ? Window : c.BackColor == SystemColors.Control || c.BackColor == SystemColors.Window ? Window : c.BackColor;
                c.ForeColor = Text;
            }

            foreach (Control child in c.Controls) ApplyTo(child);
        }

        static void ApplyTo(ToolStripItem item)
        {
            item.ForeColor = Text;
            ToolStripDropDownItem drop = item as ToolStripDropDownItem;
            if (drop != null)
            {
                drop.DropDown.BackColor = Window;
                drop.DropDown.ForeColor = Text;
                foreach (ToolStripItem sub in drop.DropDownItems) ApplyTo(sub);
            }
        }

        // Selection in menus and tool bars: the accent color (both modes); dark menus in dark mode
        sealed class Colors : ProfessionalColorTable
        {
            Color Selected { get { return Color.FromArgb(IsDark ? 110 : 60, Accent); } }
            public override Color MenuItemSelected { get { return Selected; } }
            public override Color MenuItemSelectedGradientBegin { get { return Selected; } }
            public override Color MenuItemSelectedGradientEnd { get { return Selected; } }
            public override Color MenuItemPressedGradientBegin { get { return IsDark ? Theme.Surface : base.MenuItemPressedGradientBegin; } }
            public override Color MenuItemPressedGradientEnd { get { return IsDark ? Theme.Surface : base.MenuItemPressedGradientEnd; } }
            public override Color MenuItemBorder { get { return Accent; } }
            public override Color ButtonSelectedHighlight { get { return Selected; } }
            public override Color ButtonSelectedGradientBegin { get { return Selected; } }
            public override Color ButtonSelectedGradientEnd { get { return Selected; } }
            public override Color ButtonPressedGradientBegin { get { return Accent; } }
            public override Color ButtonPressedGradientEnd { get { return Accent; } }
            public override Color ButtonSelectedBorder { get { return Accent; } }
            public override Color ToolStripDropDownBackground { get { return IsDark ? Theme.Surface : base.ToolStripDropDownBackground; } }
            public override Color ImageMarginGradientBegin { get { return IsDark ? Theme.Surface : base.ImageMarginGradientBegin; } }
            public override Color ImageMarginGradientMiddle { get { return IsDark ? Theme.Surface : base.ImageMarginGradientMiddle; } }
            public override Color ImageMarginGradientEnd { get { return IsDark ? Theme.Surface : base.ImageMarginGradientEnd; } }
            public override Color MenuBorder { get { return IsDark ? Theme.Border : base.MenuBorder; } }
            public override Color MenuStripGradientBegin { get { return IsDark ? Theme.Window : base.MenuStripGradientBegin; } }
            public override Color MenuStripGradientEnd { get { return IsDark ? Theme.Window : base.MenuStripGradientEnd; } }
            public override Color ToolStripGradientBegin { get { return IsDark ? Theme.Window : base.ToolStripGradientBegin; } }
            public override Color ToolStripGradientMiddle { get { return IsDark ? Theme.Window : base.ToolStripGradientMiddle; } }
            public override Color ToolStripGradientEnd { get { return IsDark ? Theme.Window : base.ToolStripGradientEnd; } }
            public override Color ToolStripBorder { get { return IsDark ? Theme.Window : base.ToolStripBorder; } }
            public override Color StatusStripGradientBegin { get { return IsDark ? Theme.Window : base.StatusStripGradientBegin; } }
            public override Color StatusStripGradientEnd { get { return IsDark ? Theme.Window : base.StatusStripGradientEnd; } }
            public override Color SeparatorDark { get { return IsDark ? Theme.Border : base.SeparatorDark; } }
            public override Color SeparatorLight { get { return IsDark ? Theme.Window : base.SeparatorLight; } }
        }

        // ---- Win32 ------------------------------------------------------------------

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        static extern int SetWindowTheme(IntPtr hwnd, String appName, String idList);

        [DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

        const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;   // Windows 10 before 20H1
        const int LVM_GETHEADER = 0x101F;

        static void DarkTitleBar(Form form)
        {
            try
            {
                int on = 1;
                if (DwmSetWindowAttribute(form.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int)) != 0)
                    DwmSetWindowAttribute(form.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref on, sizeof(int));
            }
            catch (Exception) { }   // older Windows: light title bar
        }

        static void DarkScrollBars(Control c)
        {
            try
            {
                SetWindowTheme(c.Handle, "DarkMode_Explorer", null);
                if (c is ListView)
                {
                    IntPtr header = SendMessage(c.Handle, LVM_GETHEADER, IntPtr.Zero, IntPtr.Zero);
                    if (header != IntPtr.Zero) SetWindowTheme(header, "DarkMode_ItemsView", null);
                }
            }
            catch (Exception) { }
        }

        static int? ReadDword(String key, String name)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(key))
                {
                    object v = k?.GetValue(name);
                    if (v is int) return (int)v;
                }
            }
            catch (Exception) { }
            return null;
        }
    }
}

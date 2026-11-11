// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    Localization.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-11
// Purpose: User interface language. English texts live in the code and the
//          designer files; lang\<language>.xml next to LiteDeploy.exe holds
//          the translations (same format as LiteTask). A missing key keeps
//          the English text.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using System.Xml;

namespace LiteDeploy
{
    static public class Localization
    {
        public const String Automatic = "auto";
        public const String English = "en";
        public const String French = "fr";

        static readonly Dictionary<String, String> translations = new Dictionary<String, String>(StringComparer.Ordinal);

        /// <summary>Language in use: "en" or "fr".</summary>
        static public String Language { get; private set; } = English;

        /// <summary>
        /// Chooses the language: the LITEDEPLOY_LANG environment variable (tests), else the user's
        /// choice ("auto", "en", "fr"); "auto" follows the Windows display language.
        /// </summary>
        static public void Initialize(String preference)
        {
            String env = Environment.GetEnvironmentVariable("LITEDEPLOY_LANG");
            String choice = !String.IsNullOrEmpty(env) ? env : preference;

            if (String.IsNullOrEmpty(choice) || choice == Automatic)
                choice = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == French ? French : English;

            Language = choice == French ? French : English;
            translations.Clear();

            if (Language != English)
                Load(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lang", Language + ".xml"));
        }

        static void Load(String file)
        {
            if (!File.Exists(file))
                return;

            try
            {
                XmlDocument doc = new XmlDocument();
                doc.Load(file);

                foreach (XmlNode node in doc.SelectNodes("/translations/translation"))
                {
                    XmlAttribute key = node.Attributes["key"];
                    if (key != null)
                        translations[key.Value] = node.InnerText;
                }
            }
            catch
            {
                // A damaged translation file leaves the interface in English
                translations.Clear();
            }
        }

        /// <summary>Translation of key, or the English text.</summary>
        static public String T(String key, String english)
        {
            String text;
            return translations.TryGetValue(key, out text) ? text : english;
        }

        /// <summary>String.Format of the translation of key (or of the English format).</summary>
        static public String F(String key, String englishFormat, params Object[] args)
        {
            return String.Format(T(key, englishFormat), args);
        }

        /// <summary>
        /// Translates a window: its title ("Form.Text") and, for every control, menu item and column
        /// header held in a field of the form, "Form.field.Text" (and ".ToolTipText" for menu items).
        /// Combo box items: "Form.field.Items.N".
        /// </summary>
        static public void Apply(Form form)
        {
            if (translations.Count == 0)
                return;

            String formName = form.GetType().Name;
            form.Text = T(formName + ".Text", form.Text);

            foreach (FieldInfo field in form.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
            {
                Object value = field.GetValue(form);
                String key = formName + "." + field.Name;

                if (value is ComboBox)
                {
                    ComboBox combo = (ComboBox)value;
                    for (int i = 0; i < combo.Items.Count; i++)
                    {
                        if (combo.Items[i] is String)
                            combo.Items[i] = T(key + ".Items." + i, (String)combo.Items[i]);
                    }
                }

                else if (value is Control && !(value is TextBoxBase) && !(value is ListControl) && !(value is NumericUpDown))
                {
                    Control control = (Control)value;
                    control.Text = T(key + ".Text", control.Text);
                }

                else if (value is ToolStripItem)
                {
                    ToolStripItem item = (ToolStripItem)value;
                    item.Text = T(key + ".Text", item.Text);
                    if (!String.IsNullOrEmpty(item.ToolTipText))
                        item.ToolTipText = T(key + ".ToolTipText", item.ToolTipText);
                }

                else if (value is ColumnHeader)
                {
                    ColumnHeader column = (ColumnHeader)value;
                    column.Text = T(key + ".Text", column.Text);
                }

                else if (value is FileDialog)
                {
                    FileDialog dialog = (FileDialog)value;
                    dialog.Title = T(key + ".Title", dialog.Title);
                }

                else if (value is FolderBrowserDialog)
                {
                    FolderBrowserDialog dialog = (FolderBrowserDialog)value;
                    dialog.Description = T(key + ".Description", dialog.Description);
                }
            }
        }
    }
}

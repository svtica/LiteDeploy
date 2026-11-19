// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    WindowLayout.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-18
// Purpose: Layout of a window kept in the user settings as text: bounds
//          ("x,y,width,height") and list columns ("name:width:position;...").
//          Pure parsing with safe defaults (an unreadable value is ignored),
//          unit-tested; the window applies what it gets.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;

namespace LiteDeploy
{
    /// <summary>Width and position (display index) of one column, by column name.</summary>
    public sealed class ColumnLayout
    {
        public String Name;
        public int Width;
        public int Position;
    }

    public static class WindowLayout
    {
        /// <summary>Smallest width kept for a column: a column dragged to 0 would be lost.</summary>
        public const int MinColumnWidth = 20;

        public static String FormatBounds(Rectangle bounds)
        {
            return String.Join(",", new[] { bounds.X, bounds.Y, bounds.Width, bounds.Height }.Select(v => v.ToString(CultureInfo.InvariantCulture)));
        }

        /// <summary>Bounds read from the settings; false when absent, unreadable or smaller than <paramref name="minimum"/>.</summary>
        public static bool TryParseBounds(String text, Size minimum, out Rectangle bounds)
        {
            bounds = Rectangle.Empty;
            if (String.IsNullOrWhiteSpace(text)) return false;
            String[] parts = text.Split(',');
            if (parts.Length != 4) return false;
            int[] v = new int[4];
            for (int i = 0; i < 4; i++)
            {
                if (!Int32.TryParse(parts[i].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v[i])) return false;
            }
            if (v[2] < minimum.Width || v[3] < minimum.Height) return false;
            bounds = new Rectangle(v[0], v[1], v[2], v[3]);
            return true;
        }

        /// <summary>True when enough of the bounds (a 100 x 50 corner of the title bar) is on one of the screens.</summary>
        public static bool IsVisibleOn(Rectangle bounds, IEnumerable<Rectangle> screens)
        {
            Rectangle title = new Rectangle(bounds.X, bounds.Y, Math.Min(100, bounds.Width), Math.Min(50, bounds.Height));
            return screens.Any(s => { Rectangle r = Rectangle.Intersect(s, title); return r.Width >= Math.Min(50, title.Width) && r.Height >= Math.Min(20, title.Height); });
        }

        public static String FormatColumns(IEnumerable<ColumnLayout> columns)
        {
            StringBuilder sb = new StringBuilder();
            foreach (ColumnLayout c in columns)
            {
                if (String.IsNullOrEmpty(c.Name) || c.Name.IndexOfAny(new[] { ':', ';' }) >= 0) continue;
                if (sb.Length > 0) sb.Append(';');
                sb.Append(c.Name).Append(':').Append(c.Width.ToString(CultureInfo.InvariantCulture))
                  .Append(':').Append(c.Position.ToString(CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        /// <summary>Names separated by commas or semicolons (hidden columns), trimmed, empty ones left out.</summary>
        public static List<String> ParseNames(String text)
        {
            if (String.IsNullOrWhiteSpace(text)) return new List<String>();
            return text.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(n => n.Trim()).Where(n => n.Length > 0).ToList();
        }

        /// <summary>Columns read from the settings, by name; unreadable entries are skipped, widths kept at least MinColumnWidth.</summary>
        public static Dictionary<String, ColumnLayout> ParseColumns(String text)
        {
            Dictionary<String, ColumnLayout> result = new Dictionary<String, ColumnLayout>(StringComparer.Ordinal);
            if (String.IsNullOrWhiteSpace(text)) return result;
            foreach (String entry in text.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                String[] parts = entry.Split(':');
                int width, position;
                if (parts.Length != 3 || parts[0].Trim().Length == 0
                    || !Int32.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out width)
                    || !Int32.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out position)
                    || position < 0)
                    continue;
                result[parts[0].Trim()] = new ColumnLayout { Name = parts[0].Trim(), Width = Math.Max(MinColumnWidth, width), Position = position };
            }
            return result;
        }

        /// <summary>
        /// Order of the columns of a list (names in their default order) after the saved positions: the saved
        /// columns by position, then the others (new columns) in their default order. Every name exactly once.
        /// </summary>
        public static List<String> Order(IList<String> defaultOrder, IDictionary<String, ColumnLayout> saved)
        {
            List<String> known = defaultOrder.Where(n => saved.ContainsKey(n)).OrderBy(n => saved[n].Position).ThenBy(n => defaultOrder.IndexOf(n)).ToList();
            return known.Concat(defaultOrder.Where(n => !saved.ContainsKey(n))).ToList();
        }
    }
}

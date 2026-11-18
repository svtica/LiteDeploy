// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    WingetShowParser.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-18
// Purpose: Reads the output of "winget show --id <Id> -e --source winget"
//          (Tools > Web versions...): version, installer type, URL and SHA256,
//          vendor page. winget translates its labels ("Installer Url",
//          "URL du programme d'installation"...), so the parsing relies on the
//          layout first: the "Name [Id]" line, the first field after it (the
//          version), the indented block of the installer (an https URL, a
//          64-digit SHA256, a known installer type). Pure logic, unit-tested
//          with English and French outputs.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace LiteDeploy
{
    /// <summary>What "winget show" says about a package (null: not in the output).</summary>
    internal sealed class WingetShowInfo
    {
        /// <summary>The "Name [Id]" line was found: the package exists in the source.</summary>
        public bool Found;
        public String Id;
        public String Version;
        public String InstallerType;
        public String InstallerUrl;
        public String InstallerSha256;
        /// <summary>Home page of the product, else the publisher page.</summary>
        public String VendorUrl;
    }

    internal static class WingetShowParser
    {
        /// <summary>Installer types of the winget manifests.</summary>
        static readonly String[] InstallerTypes = { "msix", "msi", "appx", "exe", "zip", "inno", "nullsoft", "wix", "burn", "pwa", "portable" };

        // "Found 7-Zip [7zip.7zip]", "Trouvé 7-Zip [7zip.7zip]"
        static readonly Regex Header = new Regex(@"\[(?<id>[A-Za-z0-9][A-Za-z0-9._+-]*)\]\s*$");
        static readonly Regex Sha256 = new Regex("^[0-9A-Fa-f]{64}$");
        static readonly Regex Url = new Regex(@"^https?://\S+$", RegexOptions.IgnoreCase);
        static readonly Regex AnyUrl = new Regex(@"https?://[^\s""<>]+", RegexOptions.IgnoreCase);

        // Labels known in English and French (apostrophes normalised, lower case); the layout is used otherwise
        static readonly String[] VersionLabels = { "version" };
        static readonly String[] HomepageLabels = { "homepage", "page d'accueil" };
        static readonly String[] PublisherUrlLabels = { "publisher url", "url de l'editeur", "url de l'éditeur" };

        sealed class Field
        {
            public bool Indented;
            public String Label;   // null: a line without "label: value"
            public String Value;
            public String Text;
        }

        static public WingetShowInfo Parse(String output)
        {
            WingetShowInfo info = new WingetShowInfo();
            List<Field> fields = new List<Field>();
            foreach (String raw in Lines(output))
            {
                if (!info.Found)
                {
                    Match header = Header.Match(raw.Trim());
                    if (header.Success)
                    {
                        info.Found = true;
                        info.Id = header.Groups["id"].Value;
                    }
                    continue;
                }
                if (raw.Trim().Length == 0) { fields.Add(new Field { Text = "" }); continue; }
                Field f = new Field { Indented = Char.IsWhiteSpace(raw[0]), Text = raw.Trim() };
                String label, value;
                if (SplitField(raw, out label, out value)) { f.Label = label; f.Value = value; }
                fields.Add(f);
            }
            if (!info.Found) return info;

            List<Field> top = fields.Where(f => !f.Indented && f.Label != null).ToList();
            Field version = top.FirstOrDefault(f => VersionLabels.Contains(Normalize(f.Label))) ?? top.FirstOrDefault();
            if (version != null && version.Value.Length > 0) info.Version = version.Value;

            String homepage = ValueOf(top, HomepageLabels);
            String publisherUrl = ValueOf(top, PublisherUrlLabels);
            if (homepage == null && publisherUrl == null)
            {
                // Unknown language: the first web address of the top-level lines (publisher or home page)
                Match any = fields.Where(f => !f.Indented).Select(f => AnyUrl.Match(f.Text)).FirstOrDefault(m => m.Success);
                if (any != null) publisherUrl = any.Value;
            }
            info.VendorUrl = homepage ?? publisherUrl;

            List<Field> installer = InstallerBlock(fields);
            foreach (Field f in installer)
            {
                String v = f.Value ?? "";
                if (info.InstallerSha256 == null && Sha256.IsMatch(v)) info.InstallerSha256 = v.ToLowerInvariant();
                else if (info.InstallerUrl == null && Url.IsMatch(v)) info.InstallerUrl = v;
                else if (info.InstallerType == null && InstallerTypes.Contains(v.ToLowerInvariant())) info.InstallerType = v.ToLowerInvariant();
            }
            return info;
        }

        // Indented lines of the installer section: the block holding a SHA256, else the last block
        // that follows a label without value ("Installer:", "Programme d'installation :")
        static List<Field> InstallerBlock(List<Field> fields)
        {
            List<List<Field>> blocks = new List<List<Field>>();
            List<Field> current = null;
            foreach (Field f in fields)
            {
                if (!f.Indented)
                {
                    current = f.Label != null && f.Value.Length == 0 ? new List<Field>() : null;
                    if (current != null) blocks.Add(current);
                    continue;
                }
                if (current != null) current.Add(f);
            }
            List<Field> withSha = blocks.FirstOrDefault(b => b.Any(f => f.Value != null && Sha256.IsMatch(f.Value)));
            if (withSha != null) return withSha;
            // The installer section is the last one of the output (never the Documentation links)
            List<Field> last = blocks.LastOrDefault();
            return last != null && last.Any(f => f.Value != null && Url.IsMatch(f.Value)) ? last : new List<Field>();
        }

        static String ValueOf(List<Field> top, String[] labels)
        {
            Field f = top.FirstOrDefault(x => labels.Contains(Normalize(x.Label)));
            return f != null && Url.IsMatch(f.Value) ? f.Value : null;
        }

        static String Normalize(String label)
        {
            return label.Replace('’', '\'').Replace(' ', ' ').Trim().ToLowerInvariant();
        }

        // "Label: value", "Libellé : valeur": the first colon followed by a space or the end of the
        // line (never the one of "https://")
        static bool SplitField(String line, out String label, out String value)
        {
            label = value = null;
            for (int i = 0; i < line.Length; i++)
            {
                if (line[i] != ':') continue;
                if (i + 1 < line.Length && !Char.IsWhiteSpace(line[i + 1])) continue;
                label = line.Substring(0, i).Replace(' ', ' ').Trim();
                value = line.Substring(i + 1).Trim();
                return label.Length > 0;
            }
            return false;
        }

        // Lines without the progress noise of winget (carriage returns, backspaces, spinner)
        static IEnumerable<String> Lines(String output)
        {
            foreach (String line in (output ?? "").Replace("\r\n", "\n").Split('\n'))
            {
                String l = line;
                int cr = l.LastIndexOf('\r');
                if (cr >= 0) l = l.Substring(cr + 1);
                StringBuilder sb = new StringBuilder(l.Length);
                foreach (char c in l)
                    if (!Char.IsControl(c) || c == '\t') sb.Append(c == '\t' ? ' ' : c);
                String clean = sb.ToString();
                // A line holding only the spinner or the progress bar
                if (clean.Trim().Length > 0 && clean.Trim().All(c => c == '-' || c == '\\' || c == '|' || c == '/' || c == '█' || c == '▒' || c == ' '))
                    continue;
                yield return clean.TrimEnd();
            }
        }
    }
}

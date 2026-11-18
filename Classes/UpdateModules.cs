// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    UpdateModules.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-12
// Purpose: Update modules available to LiteDeploy (docs/modules.md): PowerShell
//          scripts whose first lines say "# LiteDeploy-Module", in the modules
//          folder next to LiteDeploy.exe (shipped, signed by the release), in the
//          shared modules folder of the options (the IT team's) and in
//          %LOCALAPPDATA%\LiteDeploy\modules (the administrator's own); same
//          name: user > shared > shipped. Before the run, LiteDeploy copies the modules
//          of the run to \\computer\ADMIN$\LiteDeploy\modules, a folder only
//          administrators can write to.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LiteDeploy
{
    internal sealed class UpdateModule
    {
        public String Name;
        public String Label;
        public String[] Processes = new String[0];
        public String Path;

        /// <summary>The package line of the module: "module:Name | processes | label".</summary>
        public UpdatePackage ToPackage()
        {
            return UpdatePackage.Parse(UpdatePackage.ModulePrefix + Name + " | " + String.Join(", ", Processes) + " | " + (Label ?? Name));
        }
    }

    internal static class UpdateModules
    {
        public const String Marker = "# LiteDeploy-Module";

        static public String ShippedDir
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "modules"); }
        }

        static public String UserDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LiteDeploy", "modules"); }
        }

        /// <summary>Shared modules folder of the IT team (App > Options > Software updates); null: none.</summary>
        static public String SharedDir
        {
            get
            {
                String dir = UserSettings.Current.SharedModulesFolder;
                return String.IsNullOrWhiteSpace(dir) ? null : dir.Trim();
            }
        }

        /// <summary>The folders scanned, lowest precedence first: shipped, shared, the administrator's own.</summary>
        static public List<String> Dirs
        {
            get { return new[] { ShippedDir, SharedDir, UserDir }.Where(d => d != null).ToList(); }
        }

        /// <summary>Every module found, by name (user folder > shared folder > shipped).</summary>
        static public List<UpdateModule> List()
        {
            String warning;
            return List(out warning);
        }

        /// <summary>With <paramref name="warning"/>: why the shared folder was skipped (unreachable), or null.</summary>
        static public List<UpdateModule> List(out String warning)
        {
            List<String> skipped = new List<String>();
            List<UpdateModule> modules = Scan(Dirs, ReadDir, skipped);
            String shared = SharedDir;
            warning = shared != null && skipped.Contains(shared)
                ? Localization.F("UpdateModules.SharedUnreachable", "The shared modules folder {0} cannot be reached: its modules are not listed.", shared)
                : null;
            return modules;
        }

        /// <summary>
        /// Scans <paramref name="dirs"/> in order; same name: the later folder wins. A folder whose
        /// listing returns null (missing) or fails (unreachable share, access denied) is added to
        /// <paramref name="skipped"/> and ignored. Pure logic, unit-tested.
        /// </summary>
        static public List<UpdateModule> Scan(IEnumerable<String> dirs, Func<String, IEnumerable<UpdateModule>> readDir, ICollection<String> skipped)
        {
            Dictionary<String, UpdateModule> found = new Dictionary<String, UpdateModule>(StringComparer.OrdinalIgnoreCase);
            foreach (String dir in dirs)
            {
                List<UpdateModule> modules;
                try
                {
                    IEnumerable<UpdateModule> read = readDir(dir);
                    modules = read == null ? null : read.Where(m => m != null).ToList();
                }
                catch (IOException) { modules = null; }
                catch (UnauthorizedAccessException) { modules = null; }
                catch (System.Security.SecurityException) { modules = null; }
                catch (ArgumentException) { modules = null; }
                catch (NotSupportedException) { modules = null; }
                if (modules == null)
                {
                    if (skipped != null) skipped.Add(dir);
                    continue;
                }
                foreach (UpdateModule m in modules) found[m.Name] = m;
            }
            return found.Values.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>The modules of a folder; null when it does not exist or cannot be reached.</summary>
        static IEnumerable<UpdateModule> ReadDir(String dir)
        {
            if (!Directory.Exists(dir)) return null;
            return Directory.GetFiles(dir, "*.ps1").Select(Read);
        }

        static public UpdateModule Find(String name)
        {
            return List().FirstOrDefault(m => String.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Reads the header of a module script; null when it is not a module or its name is invalid.</summary>
        static public UpdateModule Read(String path)
        {
            try
            {
                return Parse(System.IO.Path.GetFileNameWithoutExtension(path), File.ReadLines(path).Take(20), path);
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        /// <summary>Header: "# LiteDeploy-Module", then "# Label: ..." and "# Processes: a, b". Pure logic, unit-tested.</summary>
        static public UpdateModule Parse(String name, IEnumerable<String> firstLines, String path)
        {
            if (!UpdatePackage.IsValidId(UpdatePackage.ModulePrefix + name)) return null;
            List<String> lines = firstLines.Select(l => l.Trim().TrimStart('\uFEFF')).ToList();
            if (!lines.Any(l => l.Equals(Marker, StringComparison.OrdinalIgnoreCase))) return null;

            UpdateModule m = new UpdateModule { Name = name, Label = name, Path = path };
            foreach (String line in lines)
            {
                String label = Value(line, "Label");
                if (label != null && label.Length > 0) m.Label = label.Replace("|", " ");
                String processes = Value(line, "Processes");
                if (processes != null)
                {
                    UpdatePackage probe = UpdatePackage.Parse(UpdatePackage.ModulePrefix + name + " | " + processes);
                    if (probe != null) m.Processes = probe.Processes;
                }
            }
            return m;
        }

        static String Value(String line, String key)
        {
            String prefix = "# " + key + ":";
            return line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? line.Substring(prefix.Length).Trim() : null;
        }

        /// <summary>
        /// Copies the module scripts of a run to \\computer\ADMIN$\LiteDeploy\modules. Returns null,
        /// or why it failed (the run is then not started).
        /// </summary>
        static public String CopyTo(String computer, IEnumerable<UpdatePackage> packages)
        {
            return CopyTo(computer, packages, null);
        }

        /// <summary>With <paramref name="credential"/>: the share is opened with that account (Tools > Credentials).</summary>
        static public String CopyTo(String computer, IEnumerable<UpdatePackage> packages, LiteSuite.StoredCredential credential)
        {
            List<UpdatePackage> modules = packages.Where(p => p.IsModule).ToList();
            if (modules.Count == 0) return null;
            String target = @"\\" + computer.Trim() + @"\ADMIN$\LiteDeploy\modules";
            try
            {
                using (new NetworkShare(@"\\" + computer.Trim() + @"\ADMIN$", credential))
                    return CopyModules(modules, target);
            }
            catch (Exception ex)
            {
                return Localization.F("UpdateModules.CopyFailed", "Could not copy the modules to {0}: {1}", target, ex.Message);
            }
        }

        static String CopyModules(List<UpdatePackage> modules, String target)
        {
            Directory.CreateDirectory(target);
            foreach (UpdatePackage p in modules)
            {
                UpdateModule m = Find(p.ModuleName);
                if (m == null) return Localization.F("UpdateModules.Missing", "Module {0} not found in {1}.", p.ModuleName, String.Join(", ", Dirs));
                File.Copy(m.Path, System.IO.Path.Combine(target, m.Name + ".ps1"), true);
            }
            return null;
        }
    }
}

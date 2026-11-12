// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    UpdateModules.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-12
// Purpose: Update modules available to LiteDeploy (docs/modules.md): PowerShell
//          scripts whose first lines say "# LiteDeploy-Module", in the modules
//          folder next to LiteDeploy.exe (shipped, signed by the release) and in
//          %LOCALAPPDATA%\LiteDeploy\modules (the administrator's own; same
//          name wins). Before running the worker, LiteDeploy copies the modules
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

        /// <summary>Every module found, by name (the administrator's own copy wins over the shipped one).</summary>
        static public List<UpdateModule> List()
        {
            Dictionary<String, UpdateModule> found = new Dictionary<String, UpdateModule>(StringComparer.OrdinalIgnoreCase);
            foreach (String dir in new[] { ShippedDir, UserDir })
            {
                if (!Directory.Exists(dir)) continue;
                foreach (String file in Directory.GetFiles(dir, "*.ps1"))
                {
                    UpdateModule m = Read(file);
                    if (m != null) found[m.Name] = m;
                }
            }
            return found.Values.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList();
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
            List<UpdatePackage> modules = packages.Where(p => p.IsModule).ToList();
            if (modules.Count == 0) return null;
            String target = @"\\" + computer.Trim() + @"\ADMIN$\LiteDeploy\modules";
            try
            {
                Directory.CreateDirectory(target);
                foreach (UpdatePackage p in modules)
                {
                    UpdateModule m = Find(p.ModuleName);
                    if (m == null) return Localization.F("UpdateModules.Missing", "Module {0} not found in {1} or {2}.", p.ModuleName, ShippedDir, UserDir);
                    File.Copy(m.Path, System.IO.Path.Combine(target, m.Name + ".ps1"), true);
                }
                return null;
            }
            catch (Exception ex)
            {
                return Localization.F("UpdateModules.CopyFailed", "Could not copy the modules to {0}: {1}", target, ex.Message);
            }
        }
    }
}

// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    RemoteConsole.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-18
// Purpose: "Command prompt on the computer": a local console window attached
//          to the remote computer, by the remote method of App > Options:
//          - PsExec: PsExec64.exe -accepteula -nobanner (computer) -h -s
//            cmd.exe (PsExecCommandBuilder), a command prompt as SYSTEM. With the
//            credential of the software updates: -u -p -s. PsExec has no other
//            way to receive a password than its command line (it prompts on
//            the console otherwise), as for the deployments: the command line
//            of a process is readable by the same user and by the local
//            administrators only, and it is never shown nor logged.
//          - WinRM: powershell.exe -NoExit with Enter-PSSession -ComputerName
//            PC (as the user of the credential: a remote PowerShell session,
//            not SYSTEM). The stored password is passed protected by DPAPI for
//            the current user (the format of ConvertFrom-SecureString): only
//            this Windows account, on this computer, can read it; never in
//            clear text on a command line nor in a file.
//          The argument building is pure logic (unit-tested).
// -----------------------------------------------------------------------------
using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using LiteSuite;

namespace LiteDeploy
{
    static public class RemoteConsole
    {
        /// <summary>PsExec options of the command prompt: SYSTEM, cmd.exe, 20 s to connect; the credential when one is chosen.</summary>
        static public PsExecOptions PsExecOptions(String computer, String userName, String password)
        {
            PsExecOptions o = new PsExecOptions
            {
                Computer = computer,
                RunAsSystem = true,
                ConnectionTimeout = 20,
                Executable = "cmd.exe"
            };
            if (!String.IsNullOrEmpty(userName))
            {
                o.UseCredentials = true;
                o.UserName = userName;
                o.Password = password;
                o.SystemWithCredentials = true;
            }
            return o;
        }

        /// <summary>
        /// The PowerShell command of the WinRM console. <paramref name="protectedPassword"/>: the password
        /// as ConvertFrom-SecureString writes it (DPAPI, current user); null: the current user.
        /// </summary>
        static public String PowerShellCommand(String computer, String userName, String protectedPassword)
        {
            if (String.IsNullOrWhiteSpace(computer)) throw new ArgumentException("A target computer is required.", "computer");
            StringBuilder sb = new StringBuilder();
            sb.Append("$Host.UI.RawUI.WindowTitle = ").Append(RemoteRunner.Quote(computer.Trim() + " - Enter-PSSession")).Append("; ");
            if (!String.IsNullOrEmpty(userName) && protectedPassword != null)
            {
                sb.Append("$c = New-Object System.Management.Automation.PSCredential(")
                  .Append(RemoteRunner.Quote(userName.Trim())).Append(", (ConvertTo-SecureString ")
                  .Append(RemoteRunner.Quote(protectedPassword)).Append(")); ");
                sb.Append("Enter-PSSession -ComputerName ").Append(RemoteRunner.Quote(computer.Trim())).Append(" -Credential $c; Remove-Variable c");
            }
            else
            {
                sb.Append("Enter-PSSession -ComputerName ").Append(RemoteRunner.Quote(computer.Trim()));
            }
            return sb.ToString();
        }

        /// <summary>Arguments of powershell.exe: -NoExit, the command encoded (UTF-16LE, Base64) so that no quoting can break it.</summary>
        static public String PowerShellArguments(String command)
        {
            return "-NoProfile -NoExit -ExecutionPolicy Bypass -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
        }

        /// <summary>The password as ConvertFrom-SecureString writes it without a key: DPAPI (current user) of its UTF-16LE bytes, in hexadecimal.</summary>
        static public String ProtectForPowerShell(String password)
        {
            byte[] clear = Encoding.Unicode.GetBytes(password ?? "");
            try
            {
                byte[] blob = ProtectedData.Protect(clear, null, DataProtectionScope.CurrentUser);
                StringBuilder hex = new StringBuilder(blob.Length * 2);
                foreach (byte b in blob) hex.Append(b.ToString("x2"));
                return hex.ToString();
            }
            finally
            {
                Array.Clear(clear, 0, clear.Length);
            }
        }

        /// <summary>Opens the console window of <paramref name="computer"/> (PsExec or WinRM, App > Options).</summary>
        static public void Open(String computer, StoredCredential credential)
        {
            if (RemoteRunner.Method == RemoteRunner.PsExec)
            {
                if (PsExecLocator.Path == null) throw new FileNotFoundException(Localization.T("RemoteConsole.PsExecMissing", "PsExec was not found: download it in Tools > Manage Tools, or choose the WinRM method in App > Options."));
                PsExecOptions o = PsExecOptions(computer, credential == null ? null : credential.UserName, credential == null ? null : credential.Password);
                // A console of its own (UseShellExecute): PsExec relays the remote cmd.exe there
                using (Process.Start(new ProcessStartInfo(PsExecLocator.Path, PsExecCommandBuilder.BuildArguments(o)) { UseShellExecute = true })) { }
                return;
            }

            String command = PowerShellCommand(computer, credential == null ? null : credential.UserName,
                credential == null ? null : ProtectForPowerShell(credential.Password));
            using (Process.Start(new ProcessStartInfo(PowerShellPath(), PowerShellArguments(command)) { UseShellExecute = true })) { }
        }

        static String PowerShellPath()
        {
            String path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe");
            return File.Exists(path) ? path : "powershell.exe";
        }
    }
}

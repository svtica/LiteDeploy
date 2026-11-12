// -----------------------------------------------------------------------------
// LiteSuite.Common - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    MarkOfTheWeb.cs
// Author:  LiteSuite contributors
// Date:    2026-11-12
// Purpose: Removes the Mark of the Web (the NTFS "Zone.Identifier" alternate
//          data stream Windows adds to files that come from the Internet), like
//          PowerShell Unblock-File. A marked executable started through the
//          shell shows a security prompt, and marked scripts are refused by the
//          RemoteSigned execution policy. Ported from LiteTask.
//
//          Uses the Win32 file functions: .NET Framework refuses a path that
//          contains ':' after the drive (NotSupportedException), so File.Exists
//          and File.Delete on "file:Zone.Identifier" do not work there.
// -----------------------------------------------------------------------------
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace LiteSuite
{
    public static class MarkOfTheWeb
    {
        const string StreamSuffix = ":Zone.Identifier";
        const uint InvalidFileAttributes = 0xFFFFFFFF;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern uint GetFileAttributesW(string fileName);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool DeleteFileW(string fileName);

        /// <summary>True when the file carries a Mark of the Web.</summary>
        public static bool HasMark(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return false;
            try
            {
                return GetFileAttributesW(Path.GetFullPath(filePath) + StreamSuffix) != InvalidFileAttributes;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Removes the mark from one file. Returns true when a mark was removed.</summary>
        public static bool RemoveFromFile(string filePath)
        {
            try
            {
                if (!HasMark(filePath)) return false;
                // Non-NTFS volume, access denied...: nothing to do
                return DeleteFileW(Path.GetFullPath(filePath) + StreamSuffix);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Removes the mark from every file under a folder. Returns the number of files unblocked.</summary>
        public static int RemoveFromDirectory(string folder, bool recursive = true)
        {
            int count = 0;
            try
            {
                if (!Directory.Exists(folder)) return 0;
                SearchOption scope = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                foreach (string filePath in Directory.EnumerateFiles(folder, "*", scope))
                {
                    if (RemoveFromFile(filePath)) count++;
                }
            }
            catch (Exception)
            {
            }
            return count;
        }
    }
}

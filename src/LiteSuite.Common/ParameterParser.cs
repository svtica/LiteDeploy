// -----------------------------------------------------------------------------
// LiteSuite.Common - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    ParameterParser.cs
// Author:  LiteSuite contributors
// Date:    2026-11-12
// Purpose: Parses parameter strings, ported from LiteTask. Forms that can be
//          mixed in the same string:
//            key=value
//            key="value with spaces"
//            -Name Value / -Name "Value with spaces" / -Name=Value
//            -Switch                      (no value: stored as null)
//          Keys are case-insensitive; the last occurrence wins; bare tokens
//          without '=' are skipped.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;

namespace LiteSuite
{
    public static class ParameterParser
    {
        public static Dictionary<string, object> Parse(string parameters)
        {
            Dictionary<string, object> result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(parameters)) return result;

            int i = 0;
            int len = parameters.Length;

            while (i < len)
            {
                while (i < len && char.IsWhiteSpace(parameters[i])) i++;
                if (i >= len) break;

                // PowerShell style: -Name [Value] (a dash followed by a letter)
                if (parameters[i] == '-' && i + 1 < len && IsNameStart(parameters[i + 1]))
                {
                    i++;
                    int keyStart = i;
                    while (i < len && !char.IsWhiteSpace(parameters[i]) && parameters[i] != '=') i++;
                    string psKey = parameters.Substring(keyStart, i - keyStart);

                    if (i < len && parameters[i] == '=')
                    {
                        i++;
                        result[psKey] = ReadValue(parameters, ref i);
                        continue;
                    }

                    int valueScan = i;
                    while (valueScan < len && char.IsWhiteSpace(parameters[valueScan])) valueScan++;

                    // Followed by another -Name, or by nothing: a switch
                    if (valueScan >= len ||
                        (parameters[valueScan] == '-' && valueScan + 1 < len && IsNameStart(parameters[valueScan + 1])))
                    {
                        result[psKey] = null;
                    }
                    else
                    {
                        i = valueScan;
                        result[psKey] = ReadValue(parameters, ref i);
                    }
                    continue;
                }

                // key=value
                int keyStartEq = i;
                while (i < len && parameters[i] != '=' && !char.IsWhiteSpace(parameters[i])) i++;

                if (i >= len || parameters[i] != '=')
                    continue;   // bare token without '=': skipped

                string key = parameters.Substring(keyStartEq, i - keyStartEq);
                i++;

                if (key.Length == 0)
                {
                    ReadValue(parameters, ref i);
                    continue;
                }

                result[key] = ReadValue(parameters, ref i);
            }

            return result;
        }

        static bool IsNameStart(char c)
        {
            return char.IsLetter(c) || c == '_';
        }

        static string ReadValue(string s, ref int i)
        {
            int len = s.Length;
            if (i >= len) return string.Empty;

            if (s[i] == '"')
            {
                i++;
                int valueStart = i;
                while (i < len && s[i] != '"') i++;
                string value = s.Substring(valueStart, i - valueStart);
                if (i < len) i++;
                return value;
            }

            int unquotedStart = i;
            while (i < len && !char.IsWhiteSpace(s[i])) i++;
            return s.Substring(unquotedStart, i - unquotedStart);
        }
    }
}

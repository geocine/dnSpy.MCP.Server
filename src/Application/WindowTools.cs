/*
    Copyright (C) 2026 @chichicaste
    Modifications Copyright (C) 2026 @geocine

    This file is part of dnSpy MCP Server module.

    dnSpy MCP Server is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    dnSpy MCP Server is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with dnSpy MCP Server.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using WpfApp = System.Windows.Application;

namespace dnSpy.MCP.Server.Application
{
    [Export(typeof(WindowTools))]
    public sealed class WindowTools
    {
        // ── P/Invoke ──────────────────────────────────────────────────────────

        delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        static extern bool EnumWindows(EnumWindowsProc fn, IntPtr lp);

        [DllImport("user32.dll")]
        static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc fn, IntPtr lp);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        static extern int GetWindowText(IntPtr h, StringBuilder s, int n);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        static extern int GetClassName(IntPtr h, StringBuilder s, int n);

        [DllImport("user32.dll")]
        static extern bool IsWindowVisible(IntPtr h);

        [DllImport("user32.dll")]
        static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);

        [DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);

        [DllImport("user32.dll")]
        static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);

        const uint WM_CLOSE = 0x0010;
        const uint BM_CLICK = 0x00F5;

        // ── Constructor ───────────────────────────────────────────────────────

        [ImportingConstructor]
        public WindowTools() { }

        // ── Internal helpers ──────────────────────────────────────────────────

        static string GetWndText(IntPtr h)
        {
            var sb = new StringBuilder(512);
            GetWindowText(h, sb, sb.Capacity);
            return sb.ToString();
        }

        static string GetWndClass(IntPtr h)
        {
            var sb = new StringBuilder(256);
            GetClassName(h, sb, sb.Capacity);
            return sb.ToString();
        }

        static List<(IntPtr hwnd, string text)> GetButtons(IntPtr parent)
        {
            // Collect all child buttons of a Win32 dialog.
            var buttons = new List<(IntPtr, string)>();
            EnumChildWindows(parent, (child, _) =>
            {
                if (GetWndClass(child).Equals("Button", StringComparison.OrdinalIgnoreCase))
                    buttons.Add((child, GetWndText(child).Trim()));
                return true;
            }, IntPtr.Zero);
            return buttons;
        }

        static string GetDialogMessage(IntPtr parent)
        {
            // Collect "Static" child texts for the dialog body.
            var parts = new List<string>();
            EnumChildWindows(parent, (child, _) =>
            {
                if (GetWndClass(child).Equals("Static", StringComparison.OrdinalIgnoreCase))
                {
                    string text = GetWndText(child).Trim();
                    if (text.Length > 0)
                        parts.Add(text);
                }
                return true;
            }, IntPtr.Zero);
            return string.Join(" | ", parts);
        }

        sealed class DialogInfo
        {
            public IntPtr Hwnd; // IntPtr.Zero for pure-WPF dialogs
            public string Title = "";
            public string Message = "";
            public List<string> Buttons = new List<string>();
            public bool IsWpf;
            public Window? WpfWindow; // Only for WPF dialogs
        }

        List<DialogInfo> CollectDialogs()
        {
            var result = new List<DialogInfo>();
            uint currentPid = (uint)Process.GetCurrentProcess().Id;

            // A) WPF windows (non-main windows visible on screen)
            WpfApp.Current.Dispatcher.Invoke(() =>
            {
                Window? mainWin = WpfApp.Current.MainWindow;
                foreach (Window window in WpfApp.Current.Windows)
                {
                    if (!window.IsVisible || window == mainWin)
                        continue;

                    var dialog = new DialogInfo
                    {
                        IsWpf = true,
                        WpfWindow = window,
                        Title = window.Title ?? "",
                        // WPF MessageBox wraps a Win32 dialog; collect buttons below.
                        Message = "",
                        Buttons = new List<string>()
                    };

                    // Try to get the underlying HWND, which may not exist for pure WPF windows.
                    try
                    {
                        var interop = new System.Windows.Interop.WindowInteropHelper(window);
                        IntPtr hwnd = interop.Handle;
                        if (hwnd != IntPtr.Zero && IsWindow(hwnd))
                        {
                            dialog.Hwnd = hwnd;
                            dialog.Message = GetDialogMessage(hwnd);
                            foreach (var (_, text) in GetButtons(hwnd))
                            {
                                if (text.Length > 0)
                                    dialog.Buttons.Add(text);
                            }
                        }
                    }
                    catch {
                        // Ignore transient interop failures while probing dialog metadata.
                    }

                    result.Add(dialog);
                }
            });

            // B) Win32 dialogs (class #32770) owned by this process
            var seenHwnds = new HashSet<IntPtr>();
            foreach (var dialog in result)
            {
                if (dialog.Hwnd != IntPtr.Zero)
                    seenHwnds.Add(dialog.Hwnd);
            }

            EnumWindows((hwnd, _) =>
            {
                if (!IsWindowVisible(hwnd))
                    return true;

                GetWindowThreadProcessId(hwnd, out uint pid);
                if (pid != currentPid)
                    return true;
                if (!GetWndClass(hwnd).Equals("#32770", StringComparison.Ordinal))
                    return true;
                if (seenHwnds.Contains(hwnd))
                    return true;

                var dialog = new DialogInfo
                {
                    IsWpf = false,
                    Hwnd = hwnd,
                    Title = GetWndText(hwnd),
                    Message = GetDialogMessage(hwnd)
                };
                foreach (var (_, text) in GetButtons(hwnd))
                {
                    if (text.Length > 0)
                        dialog.Buttons.Add(text);
                }

                result.Add(dialog);
                return true;
            }, IntPtr.Zero);

            return result;
        }

        // ── Public tools ──────────────────────────────────────────────────────

        public string ListDialogs(Dictionary<string, object>? args = null)
        {
            var dialogs = CollectDialogs();
            if (dialogs.Count == 0)
                return "No active dialogs.";

            var sb = new StringBuilder();
            for (int i = 0; i < dialogs.Count; i++)
            {
                var dialog = dialogs[i];
                sb.AppendLine($"[{i + 1}] Title: \"{dialog.Title}\"");
                if (dialog.Hwnd != IntPtr.Zero)
                    sb.AppendLine($"    Hwnd: {dialog.Hwnd.ToInt64():X}  |  Type: {(dialog.IsWpf ? "WPF" : "Win32 (#32770)")}");
                else
                    sb.AppendLine("    Type: WPF (no HWND)");

                if (!string.IsNullOrWhiteSpace(dialog.Message))
                    sb.AppendLine($"    Message: \"{dialog.Message}\"");
                if (dialog.Buttons.Count > 0)
                    sb.AppendLine($"    Buttons: {string.Join(", ", dialog.Buttons)}");
                sb.AppendLine();
            }

            return sb.ToString().TrimEnd();
        }

        public string CloseDialog(Dictionary<string, object>? args)
        {
            args ??= new Dictionary<string, object>();

            // Parse optional hwnd.
            IntPtr targetHwnd = IntPtr.Zero;
            if (args.TryGetValue("hwnd", out object? hwndObj) && hwndObj is not null)
            {
                string hwndStr = hwndObj is JsonElement je ? je.GetString() ?? "" : hwndObj.ToString() ?? "";
                hwndStr = hwndStr.Trim();
                if (hwndStr.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    hwndStr = hwndStr.Substring(2);
                if (long.TryParse(hwndStr, System.Globalization.NumberStyles.HexNumber, null, out long value))
                    targetHwnd = new IntPtr(value);
            }

            // Parse optional button preference.
            string buttonPref = "ok";
            if (args.TryGetValue("button", out object? btnObj) && btnObj is not null)
            {
                string raw = btnObj is JsonElement je2 ? je2.GetString() ?? "" : btnObj.ToString() ?? "";
                if (raw.Length > 0)
                    buttonPref = raw.Trim().ToLowerInvariant();
            }

            var dialogs = CollectDialogs();
            if (dialogs.Count == 0)
                return "No active dialogs.";

            // Resolve target dialog.
            DialogInfo? target = null;
            if (targetHwnd != IntPtr.Zero)
            {
                foreach (var dialog in dialogs)
                {
                    if (dialog.Hwnd == targetHwnd)
                    {
                        target = dialog;
                        break;
                    }
                }

                if (target == null)
                    return $"Error: dialog with HWND {targetHwnd.ToInt64():X} was not found.";
            }
            else
            {
                target = dialogs[0];
            }

            if (target.Hwnd != IntPtr.Zero)
            {
                if (!IsWindow(target.Hwnd))
                    return $"Error: HWND {target.Hwnd.ToInt64():X} is no longer valid (window already closed).";

                // Click a matching button on a Win32 dialog if one exists.
                var buttons = GetButtons(target.Hwnd);
                IntPtr matchedBtn = IntPtr.Zero;
                string matchedText = "";

                foreach (var (btnHwnd, btnText) in buttons)
                {
                    if (ButtonMatches(buttonPref, btnText))
                    {
                        matchedBtn = btnHwnd;
                        matchedText = btnText;
                        break;
                    }
                }

                if (matchedBtn != IntPtr.Zero)
                {
                    SendMessage(matchedBtn, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
                    return $"Clicked '{matchedText}' in dialog '{target.Title}'.";
                }

                // Fallback: close the window when no button matched.
                PostMessage(target.Hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                return $"No matching button for '{buttonPref}' found; sent WM_CLOSE to dialog '{target.Title}'.";
            }

            // Pure WPF window without HWND: close via Dispatcher.
            if (target.WpfWindow != null)
            {
                WpfApp.Current.Dispatcher.Invoke(() => target.WpfWindow.Close());
                return $"Closed WPF dialog '{target.Title}'.";
            }

            return "Error: dialog has neither an HWND nor a WPF window reference.";
        }

        // ── Button matching ───────────────────────────────────────────────────

        static bool ButtonMatches(string pref, string btnText)
        {
            string lower = btnText.ToLowerInvariant();
            switch (pref)
            {
                case "ok":
                case "accept":
                    return lower == "ok" || lower == "accept";

                case "yes":
                    return lower == "yes";

                case "no":
                    return lower == "no";

                case "cancel":
                    return lower == "cancel";

                case "retry":
                    return lower == "retry";

                case "ignore":
                    return lower == "ignore";

                default:
                    return lower.Contains(pref);
            }
        }
    }
}

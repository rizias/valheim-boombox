using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using BepInEx.Configuration;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using VikingBoombox;

public sealed partial class QaGameplay
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    private static IntPtr ShortcutWindow()
    {
        uint pid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
        IntPtr found = IntPtr.Zero;
        int size = 0;
        EnumWindows(
        (handle, state) =>
            {
                GetWindowThreadProcessId(handle, out uint owner);
                if (owner == pid && GetClientRect(handle, out var rect))
                {
                    int area = (rect.right - rect.left) * (rect.bottom - rect.top);
                    if (area > size)
                    {
                        size = area;
                        found = handle;
                    }
                }
                return true;
            },
        IntPtr.Zero
        );
        return found;
    }
    private IEnumerator ShortcutPulse(System.Diagnostics.Stopwatch clock, IntPtr window, int hold, params Key[] keys)
    {
        Need(
            clock.Elapsed.TotalSeconds < 1.6 && Application.isFocused && GetForegroundWindow() == window,
            "C09 native focus lease ended"
        );
        InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(keys));
        for (int i = 0; i < hold; i++)
            yield return null;
        InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
        yield return null;
        yield return null;
        Need(
            clock.Elapsed.TotalSeconds < 1.8 && Application.isFocused && GetForegroundWindow() == window,
            "C09 focus unavailable during sequence"
        );
    }
    private IEnumerator CaseShortcuts(bool manualActivation = false)
    {
        var plugin = BoomboxPlugin.Instance;
        var oldMenu = plugin.MenuShortcut.Value;
        var oldPlay = plugin.PlayPauseShortcut.Value;
        var input = InputSystem.settings.backgroundBehavior;
        var proof =
            new JObject
            {
                ["case"] = "C-09",
                ["scope"] =
                    "Actual native foreground focus for at most two seconds, internal InputSystem key events through ordinary plugin Update; no patch of Application.isFocused or shortcut handlers."
            };
        lastCapture = proof;
        IntPtr previous = IntPtr.Zero, window = IntPtr.Zero;
        System.Threading.Timer timer = null;
        var clock = new System.Diagnostics.Stopwatch();
        long restoredAt = 0;
        void Release()
        {
            if (window == IntPtr.Zero || GetForegroundWindow() != window)
                return;
            if (previous == IntPtr.Zero || !SetForegroundWindow(previous))
                ShowWindow(window, 0);
            System.Threading.Interlocked.CompareExchange(ref restoredAt, clock.ElapsedTicks, 0);
        }
        try
        {
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            if (!Keyboard.current.enabled)
                InputSystem.EnableDevice(Keyboard.current);
            yield return CaseReadyInventory();
            Create("C09", new[] { 0, 1, 0, 0, 0 }, 0, false, Player.m_localPlayer.transform.position);
            var deck = decks["C09"];
            plugin.MenuShortcut.Value = new KeyboardShortcut(KeyCode.F7, KeyCode.LeftControl);
            plugin.PlayPauseShortcut.Value = new KeyboardShortcut(KeyCode.F4, KeyCode.LeftControl);
            plugin.Config.Save();
            string saved = File.ReadAllText(plugin.Config.ConfigFilePath);
            Need(
                saved.Contains("MenuShortcut = " + plugin.MenuShortcut.GetSerializedValue())
                && saved.Contains("PlayPauseShortcut = " + plugin.PlayPauseShortcut.GetSerializedValue()),
                "C09 assignments not saved"
            );
            proof["assignmentsSaved"] = true;
            plugin.OpenOwn();
            yield return new WaitForSecondsRealtime(.4f);
            Need((bool)UiDispatch(UiButton("Tab1"), false)["hitsTarget"], "C09 prewarm playlist tab failed");
            yield return new WaitForSecondsRealtime(.3f);
            plugin.CloseMenu();
            InventoryGui.instance.Hide();
            EventSystem.current.SetSelectedGameObject(null);
            yield return null;
            var status = JObject.Parse(File.ReadAllText(Path.Combine(BepInEx.Paths.ConfigPath, "qa-status.json")));
            Need(
                (bool)status["quiet"]["audioSessionMuted"] && (bool)status["quiet"]["windowOffscreen"],
                "C09 quiet mode missing"
            );
            window = ShortcutWindow();
            Need(window != IntPtr.Zero, "C09 native game window missing");
            previous = GetForegroundWindow();
            bool granted;
            if (manualActivation)
            {
                proof["awaitingUserActivation"] = true;
                float until = Time.realtimeSinceStartup + 45;
                while (GetForegroundWindow() != window && Time.realtimeSinceStartup < until)
                    yield return null;
                proof["awaitingUserActivation"] = false;
                granted = GetForegroundWindow() == window;
                clock.Start();
                timer = new System.Threading.Timer(_ => Release(), null, 1600, System.Threading.Timeout.Infinite);
            }
            else
            {
                clock.Start();
                timer = new System.Threading.Timer(_ => Release(), null, 1600, System.Threading.Timeout.Infinite);
                granted = SetForegroundWindow(window);
            }
            proof["focusRequested"] = granted;
            proof["manualActivation"] = manualActivation;
            for (int n = 0; n < 5 && !Application.isFocused; n++)
                yield return null;
            if (!granted || !Application.isFocused || GetForegroundWindow() != window)
            {
                proof["blocked"] = "Windows did not grant native focus within the single permitted attempt";
                proof["passed"] = false;
                yield break;
            }
            yield return ShortcutPulse(clock, window, 2, Key.F7);
            Need(!plugin.MenuOpen, "C09 bare menu key acted");
            yield return ShortcutPulse(clock, window, 4, Key.LeftCtrl, Key.F7);
            Need(plugin.MenuOpen, "C09 modified menu key/hold failed");
            yield return ShortcutPulse(clock, window, 2, Key.LeftCtrl, Key.F7);
            Need(!plugin.MenuOpen, "C09 modified menu close failed");
            bool playing = deck.State.playing;
            yield return ShortcutPulse(clock, window, 2, Key.F4);
            Need(deck.State.playing == playing, "C09 bare playback key acted");
            yield return ShortcutPulse(clock, window, 4, Key.LeftCtrl, Key.F4);
            Need(deck.State.playing != playing, "C09 playback modifier/hold failed");
            yield return ShortcutPulse(clock, window, 2, Key.LeftCtrl, Key.F4);
            Need(deck.State.playing == playing, "C09 playback resume failed");
            yield return ShortcutPulse(clock, window, 2, Key.LeftCtrl, Key.F7);
            Need(plugin.MenuOpen, "C09 text-entry menu open failed");
            var field = UiInput("PlaylistNewName");
            EventSystem.current.SetSelectedGameObject(field.gameObject);
            field.ActivateInputField();
            yield return null;
            yield return null;
            Need(field.isFocused, "C09 text field did not focus");
            yield return ShortcutPulse(clock, window, 2, Key.LeftCtrl, Key.F4);
            Need(deck.State.playing == playing, "C09 text entry toggled playback");
            yield return ShortcutPulse(clock, window, 2, Key.LeftCtrl, Key.F7);
            Need(plugin.MenuOpen, "C09 text entry toggled menu");
            proof["modifiersPassed"] = true;
            proof["holdSingleTogglePassed"] = true;
            proof["textFocusPassed"] = true;
            proof["passed"] = true;
        }
        finally
        {
            Release();
            timer?.Dispose();
            proof["focusMilliseconds"] = restoredAt > 0
                ? restoredAt * 1000.0 / System.Diagnostics.Stopwatch.Frequency
                : 0;
            proof["gameNoLongerForeground"] = window == IntPtr.Zero || GetForegroundWindow() != window;
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
            plugin.MenuShortcut.Value = oldMenu;
            plugin.PlayPauseShortcut.Value = oldPlay;
            plugin.Config.Save();
            plugin.CloseMenu();
            InventoryGui.instance.Hide();
            Freeze(Player.m_localPlayer, false);
            InputSystem.settings.backgroundBehavior = input;
            Need(
                (bool)proof["gameNoLongerForeground"] && (double)proof["focusMilliseconds"] <= 2000,
                "C09 focus restore/limit failed"
            );
        }
    }
}

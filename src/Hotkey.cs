using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ShreddersNightAurora
{
    /// <summary>
    /// One rebindable key. Uses the legacy Input Manager when the game has it enabled and falls back to the
    /// Input System package otherwise (Unity throws InvalidOperationException from Input.* when legacy input is off).
    /// </summary>
    internal sealed class Hotkey
    {
        static bool legacyUnavailable;

        KeyCode keyCode = KeyCode.None;
        Key key = Key.None;

        public void Bind(string name)
        {
            keyCode = Enum.TryParse(name, true, out KeyCode kc) ? kc : KeyCode.None;
            key = Enum.TryParse(name, true, out Key k) ? k : Key.None;
            if (keyCode == KeyCode.None && key == Key.None)
                Mod.Log.Warning($"Unknown key '{name}'; that toggle is disabled.");
        }

        public bool Pressed()
        {
            if (!legacyUnavailable && keyCode != KeyCode.None)
            {
                try { return Input.GetKeyDown(keyCode); }
                catch (Exception)
                {
                    legacyUnavailable = true;
                    Mod.Log.Msg("Legacy Input disabled in this build; using the Input System for hotkeys.");
                }
            }
            if (key == Key.None) return false;
            var keyboard = Keyboard.current;
            return keyboard != null && keyboard[key].wasPressedThisFrame;
        }
    }
}

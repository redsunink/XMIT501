using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using SharpDX.DirectInput;

namespace XMIT501_CS
{
    public class InputManager
    {
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private DirectInput _directInput;
        private List<Joystick> _joysticks;

        public InputManager()
        {
            _directInput = new DirectInput();
            _joysticks = new List<Joystick>();
            RefreshJoysticks();
        }

        public void RefreshJoysticks()
        {
            foreach (var joy in _joysticks) joy.Dispose();
            _joysticks.Clear();

            // GameControl catches Joysticks, Gamepads, HOTAS, and Throttles natively
            var devices = _directInput.GetDevices(DeviceClass.GameControl, DeviceEnumerationFlags.AttachedOnly);

            foreach (var device in devices)
            {
                try
                {
                    var joystick = new Joystick(_directInput, device.InstanceGuid);
                    joystick.Acquire();
                    _joysticks.Add(joystick);
                }
                catch { /* Ignore devices that Windows has locked */ }
            }
        }

        // Checks if a specific multi-key keyboard combo is held
        public bool IsKeyboardBoundAndPressed(List<int> virtualKeys)
        {
            if (virtualKeys == null || virtualKeys.Count == 0) return false;
            return virtualKeys.All(vk => (GetAsyncKeyState(vk) & 0x8000) != 0);
        }

        // Checks if a specific joystick button is held
        public bool IsJoystickBoundAndPressed(string targetGuid, int targetButton)
        {
            if (string.IsNullOrEmpty(targetGuid) || targetButton < 0) return false;

            foreach (var joy in _joysticks)
            {
                if (joy.Information.InstanceGuid.ToString() == targetGuid)
                {
                    try
                    {
                        joy.Poll();
                        return joy.GetCurrentState().Buttons[targetButton];
                    }
                    catch { return false; } // Device unplugged mid-game
                }
            }
            return false;
        }

        // Used during the Binding phase to find the first pressed controller button
        public (string Guid, int Button, string DeviceName)? GetAnyJoystickButtonPressed()
        {
            foreach (var joy in _joysticks)
            {
                try
                {
                    joy.Poll();
                    var buttons = joy.GetCurrentState().Buttons;
                    for (int i = 0; i < buttons.Length; i++)
                    {
                        if (buttons[i]) return (joy.Information.InstanceGuid.ToString(), i, joy.Information.InstanceName);
                    }
                }
                catch { }
            }
            return null;
        }
    }
}
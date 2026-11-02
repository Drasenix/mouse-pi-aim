using System;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;
using MouseDevice = UnityEngine.InputSystem.Mouse;

namespace MouseAim
{
    public class AimController : MonoBehaviour
    {
        private const string ModifierId = "mouse-aim";

        private static bool s_isAiming;
        private static float s_spreadMultiplier = 1f;

        private Harmony _harmony;
        private CameraFOVController _fovController;
        private AimFOVModifier _fovModifier;
        private bool _aimActive;
        private bool _bindingWarningLogged;

        private void Awake()
        {
            s_isAiming = false;
            s_spreadMultiplier = 1f;

            _harmony = new Harmony("com.cono.mouseaim.spread");

            try
            {
                MethodInfo target = AccessTools.Method(
                    typeof(PlayerWeaponController),
                    nameof(PlayerWeaponController.TotalSpread),
                    Type.EmptyTypes);

                if (target == null)
                {
                    LogWarning("[MouseAim] TotalSpread not found; accuracy bonus disabled");
                    return;
                }

                MethodInfo postfix = typeof(AimController).GetMethod(
                    nameof(TotalSpreadPostfix),
                    BindingFlags.Static | BindingFlags.NonPublic);

                if (postfix == null)
                {
                    LogWarning("[MouseAim] spread postfix not found");
                    return;
                }

                _harmony.Patch(target, postfix: new HarmonyMethod(postfix));
                LogInfo("[MouseAim] accuracy patch applied");
            }
            catch (Exception e)
            {
                LogWarning("[MouseAim] accuracy patch failed: " + e.Message);
            }
        }

        private void Update()
        {
            Plugin plugin = Plugin.Instance;
            if (plugin == null)
                return;

            s_spreadMultiplier = Mathf.Clamp01(plugin.AimSpreadMultiplier.Value);

            if (!plugin.Enabled.Value)
            {
                StopAim();
                return;
            }

            if (!TryReadAimButton(plugin.AimButton.Value, out bool held))
            {
                if (!_bindingWarningLogged)
                {
                    _bindingWarningLogged = true;
                    LogWarning("[MouseAim] invalid AimButton '" + plugin.AimButton.Value + "'");
                }
                StopAim();
                return;
            }

            if (!held || !CanAim())
            {
                StopAim();
                return;
            }

            StartAim();
        }

        private void OnDestroy()
        {
            s_isAiming = false;
            s_spreadMultiplier = 1f;

            if (_fovController != null)
                _fovController.RemoveModifier(ModifierId);

            if (_harmony != null)
                _harmony.UnpatchSelf();
        }

        private static void TotalSpreadPostfix(ref float __result)
        {
            if (s_isAiming)
                __result *= s_spreadMultiplier;
        }

        private static bool CanAim()
        {
            Plugin plugin = Plugin.Instance;
            if (plugin == null)
                return false;

            Player player = Player.Instance;
            if (player == null)
                return false;

            if (Cursor.lockState != CursorLockMode.Locked)
                return false;

            if (!Application.isFocused)
                return false;

            if (Time.timeScale <= 0f)
                return false;

            PlayerPhotoCameraController photo = player.PhotoCameraController;
            if (photo != null && photo.IsUsingCamera)
                return false;

            PlayerWeaponController weapons = player.WeaponController;
            if (weapons == null)
                return false;

            if (plugin.RequireWeaponDrawn.Value)
            {
                if (weapons.CurrentWeapon == null)
                    return false;

                if (weapons.WeaponHolstered || weapons.WeaponHolsteredRequestedState)
                    return false;
            }

            WeaponState state = weapons.CurrentWeaponState;
            if (state == WeaponState.SecondaryFiring ||
                state == WeaponState.Misc ||
                state == WeaponState.Changing)
                return false;

            return true;
        }

        private void StartAim()
        {
            Plugin plugin = Plugin.Instance;
            if (plugin == null)
                return;

            if (!_aimActive)
            {
                _aimActive = true;
                s_isAiming = true;

                if (plugin.Verbose.Value)
                    LogInfo("[MouseAim] aim start");
            }

            EnsureModifier();
        }

        private void StopAim()
        {
            if (!_aimActive)
            {
                s_isAiming = false;
                return;
            }

            _aimActive = false;
            s_isAiming = false;

            if (_fovModifier != null)
                _fovModifier.SetActive(false);

            Plugin plugin = Plugin.Instance;
            if (plugin != null && plugin.Verbose.Value)
                LogInfo("[MouseAim] aim stop");
        }

        private void EnsureModifier()
        {
            Plugin plugin = Plugin.Instance;
            if (plugin == null)
                return;

            Player player = Player.Instance;
            if (player == null)
                return;

            CameraFOVController controller = player.CameraFovController;
            if (controller == null)
                return;

            if (!ReferenceEquals(controller, _fovController) || _fovModifier == null)
            {
                if (_fovController != null)
                    _fovController.RemoveModifier(ModifierId);

                _fovController = controller;
                _fovModifier = new AimFOVModifier();
                controller.AddModifier(ModifierId, _fovModifier);
            }
            else
            {
                controller.AddModifier(ModifierId, _fovModifier);
            }

            _fovModifier.SetTarget(-plugin.AimZoomDegrees.Value, plugin.AimTransitionSeconds.Value);
            _fovModifier.SetActive(true);
        }

        private static bool TryReadAimButton(string binding, out bool held)
        {
            held = false;

            if (string.IsNullOrWhiteSpace(binding))
                return false;

            string normalized = binding.Trim()
                .Replace("<", string.Empty)
                .Replace(">", string.Empty)
                .Replace("/", string.Empty)
                .Replace(" ", string.Empty)
                .ToLowerInvariant();

            if (normalized.StartsWith("keyboard", StringComparison.Ordinal))
                normalized = normalized.Substring("keyboard".Length);

            switch (normalized)
            {
                case "middlemouse":
                case "middlebutton":
                case "mousemiddlebutton":
                    held = MouseDevice.current != null && MouseDevice.current.middleButton.isPressed;
                    return true;

                case "rightmouse":
                case "rightbutton":
                case "mouserightbutton":
                    held = MouseDevice.current != null && MouseDevice.current.rightButton.isPressed;
                    return true;

                case "leftmouse":
                case "leftbutton":
                case "mouseleftbutton":
                    held = MouseDevice.current != null && MouseDevice.current.leftButton.isPressed;
                    return true;

                case "mouse4":
                case "mouseback":
                case "mousebackbutton":
                    held = MouseDevice.current != null && MouseDevice.current.backButton.isPressed;
                    return true;

                case "mouse5":
                case "mouseforward":
                case "mouseforwardbutton":
                    held = MouseDevice.current != null && MouseDevice.current.forwardButton.isPressed;
                    return true;
            }

            if (Enum.TryParse(normalized, true, out Key key))
            {
                held = Keyboard.current != null && Keyboard.current[key].isPressed;
                return true;
            }

            return false;
        }

        private static void LogInfo(string message)
        {
            Plugin plugin = Plugin.Instance;
            if (plugin != null)
                plugin.Log.LogInfo(message);
        }

        private static void LogWarning(string message)
        {
            Plugin plugin = Plugin.Instance;
            if (plugin != null)
                plugin.Log.LogWarning(message);
        }
    }

    internal sealed class AimFOVModifier : IFOVModifier
    {
        private bool _active;
        private float _current;
        private float _target;
        private float _duration = 0.15f;
        private float _velocity;

        public bool KeepRunning => _active || Mathf.Abs(_current) > 0.01f;

        public float Update()
        {
            float target = _active ? _target : 0f;

            _current = Mathf.SmoothDamp(
                _current,
                target,
                ref _velocity,
                Mathf.Max(0.01f, _duration),
                float.PositiveInfinity,
                Time.unscaledDeltaTime);

            if (!_active && Mathf.Abs(_current) < 0.01f)
            {
                _current = 0f;
                _velocity = 0f;
            }

            return _current;
        }

        public void SetActive(bool active)
        {
            _active = active;
        }

        public void SetTarget(float target, float duration)
        {
            _target = target;
            _duration = Mathf.Max(0.01f, duration);
        }
    }
}

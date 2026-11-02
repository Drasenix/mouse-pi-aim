using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MouseAim
{
    [DefaultExecutionOrder(-500)]
    public class AimController : MonoBehaviour
    {
        private const string ModifierId = "mouse-aim";

        private static bool s_isAiming;
        private static float s_spreadMultiplier = 1f;

        private Harmony _harmony;
        private CameraFOVController _fovController;
        private AimFOVModifier _fovModifier;
        private bool _aimActive;

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
            MenuIntegration.Tick();

            Plugin plugin = Plugin.Instance;
            if (plugin == null)
                return;

            s_spreadMultiplier = Mathf.Clamp01(plugin.AimSpreadMultiplier.Value);

            if (!plugin.Enabled.Value)
            {
                StopAim("disabled");
                return;
            }

            InputAction aimAction = MenuIntegration.AimAction;
            if (aimAction == null)
            {
                StopAim("action not ready");
                return;
            }

            if (!aimAction.IsPressed())
            {
                StopAim("released");
                return;
            }

            if (!CanAim(out string reason))
            {
                StopAim(reason);
                return;
            }

            StartAim();
        }

        private void OnDestroy()
        {
            s_isAiming = false;
            s_spreadMultiplier = 1f;

            MenuIntegration.Shutdown();

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

        private static bool CanAim(out string reason)
        {
            reason = null;

            Plugin plugin = Plugin.Instance;
            if (plugin == null)
            {
                reason = "no plugin";
                return false;
            }

            Player player = Player.Instance;
            if (player == null)
            {
                reason = "no player";
                return false;
            }

            if (Cursor.lockState != CursorLockMode.Locked)
            {
                reason = "cursor unlocked";
                return false;
            }

            if (!Application.isFocused)
            {
                reason = "not focused";
                return false;
            }

            if (Time.timeScale <= 0f)
            {
                reason = "paused";
                return false;
            }

            PlayerPhotoCameraController photo = player.PhotoCameraController;
            if (photo != null && photo.IsUsingCamera)
            {
                reason = "photo camera";
                return false;
            }

            PlayerWeaponController weapons = player.WeaponController;
            if (weapons == null)
            {
                reason = "no weapon controller";
                return false;
            }

            if (plugin.RequireWeaponDrawn.Value)
            {
                if (weapons.CurrentWeapon == null)
                {
                    reason = "no weapon";
                    return false;
                }

                if (weapons.WeaponHolstered || weapons.WeaponHolsteredRequestedState)
                {
                    reason = "weapon holstered";
                    return false;
                }
            }

            WeaponState state = weapons.CurrentWeaponState;
            if (state == WeaponState.SecondaryFiring)
            {
                reason = "secondary ability";
                return false;
            }

            if (state == WeaponState.Misc)
            {
                reason = "misc state";
                return false;
            }

            if (state == WeaponState.Changing)
            {
                reason = "changing weapon";
                return false;
            }

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
                {
                    LogInfo("[MouseAim] aim start (key=" + plugin.AimButton.Value +
                            ", zoom=" + plugin.AimZoomDegrees.Value.ToString("0.0") +
                            ", spread=" + plugin.AimSpreadMultiplier.Value.ToString("0.00") + ")");
                }
            }

            EnsureModifier();
        }

        private void StopAim(string reason)
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
                LogInfo("[MouseAim] aim stop (" + reason + ")");
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
        private float _duration = 0.08f;

        public bool KeepRunning => _active || Mathf.Abs(_current) > 0.01f;

        public float Update()
        {
            float target = _active ? _target : 0f;
            float speed = 1f / Mathf.Max(0.01f, _duration);
            float t = 1f - Mathf.Exp(-speed * Time.unscaledDeltaTime);

            _current = Mathf.Lerp(_current, target, t);

            if (!_active && Mathf.Abs(_current) < 0.01f)
                _current = 0f;
            else if (_active && Mathf.Abs(_current - _target) < 0.01f)
                _current = _target;

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

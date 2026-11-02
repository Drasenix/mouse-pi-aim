using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using MouseDevice = UnityEngine.InputSystem.Mouse;

namespace MouseAim
{
    internal static class MenuIntegration
    {
        private const string MenuLabel = "Aim key";
        private const float CaptureDelay = 0.1f;
        private const float CaptureTimeout = 10f;

        private static ManualLogSource _logger;
        private static bool _verbose;

        private static Type _settingType;
        private static object _setting;
        private static MethodInfo _alertOnValueChanged;

        private static Type _pageType;
        private static Type _rowType;
        private static Type _buttonType;
        private static Type _textType;
        private static object _rowNameText;
        private static object _rowNameRow;

        private static Harmony _harmony;
        private static bool _patchAttempted;
        private static bool _setupPatched;
        private static bool _updateNamePatched;
        private static bool _buttonClickPatched;
        private static bool _buttonRefreshPatched;
        private static bool _textPatched;
        private static bool _injected;
        private static float _nextTryTime;
        private static float _nextSettingTryTime;
        private static float _nextPatchTryTime;
        private static bool _errorLogged;

        private static bool s_isCapturing;
        private static float s_captureStartTime;

        public static bool IsCapturing => s_isCapturing;

        public static void Initialize(ManualLogSource logger, bool verbose)
        {
            _logger = logger;
            _verbose = verbose;

            CreateSetting();
            ApplyPatches();
            TryInject();
        }

        public static void Tick()
        {
            if (_setting == null && Time.unscaledTime >= _nextSettingTryTime)
            {
                _nextSettingTryTime = Time.unscaledTime + 2f;
                CreateSetting();
            }

            if (!_patchAttempted && Time.unscaledTime >= _nextPatchTryTime)
            {
                _nextPatchTryTime = Time.unscaledTime + 2f;
                ApplyPatches();
            }

            if (!_injected && Time.unscaledTime >= _nextTryTime)
            {
                _nextTryTime = Time.unscaledTime + 2f;
                TryInject();
            }
        }

        public static void UpdateKeyCapture()
        {
            if (!s_isCapturing)
                return;

            float elapsed = Time.unscaledTime - s_captureStartTime;
            if (elapsed < CaptureDelay)
                return;

            if (elapsed > CaptureTimeout)
            {
                CancelKeyCapture("timeout");
                return;
            }

            if (Keyboard.current != null)
            {
                KeyControl escape = Keyboard.current[Key.Escape];
                if (escape != null && escape.wasPressedThisFrame)
                {
                    CancelKeyCapture("escape");
                    return;
                }

                foreach (KeyControl control in Keyboard.current.allKeys)
                {
                    if (control != null && control.wasPressedThisFrame)
                    {
                        SetCapturedKey(control.keyCode.ToString());
                        return;
                    }
                }
            }

            if (MouseDevice.current != null)
            {
                if (MouseDevice.current.middleButton.wasPressedThisFrame)
                {
                    SetCapturedKey("MiddleMouse");
                    return;
                }

                if (MouseDevice.current.rightButton.wasPressedThisFrame)
                {
                    SetCapturedKey("RightMouse");
                    return;
                }

                if (MouseDevice.current.backButton.wasPressedThisFrame)
                {
                    SetCapturedKey("Mouse4");
                    return;
                }

                if (MouseDevice.current.forwardButton.wasPressedThisFrame)
                {
                    SetCapturedKey("Mouse5");
                    return;
                }
            }
        }

        private static void BeginKeyCapture()
        {
            if (s_isCapturing)
                return;

            s_isCapturing = true;
            s_captureStartTime = Time.unscaledTime;
            RefreshSettingLabel();

            if (_verbose)
                _logger.LogInfo("[MouseAim] waiting for aim key...");
        }

        private static void SetCapturedKey(string key)
        {
            s_isCapturing = false;

            Plugin plugin = Plugin.Instance;
            if (plugin != null)
            {
                plugin.AimButton.Value = key;
                plugin.Config.Save();

                if (plugin.Verbose.Value)
                    _logger.LogInfo("[MouseAim] aim key captured: " + key);
            }

            RefreshSettingLabel();
        }

        private static void CancelKeyCapture(string reason)
        {
            s_isCapturing = false;
            RefreshSettingLabel();

            if (_verbose)
                _logger.LogInfo("[MouseAim] aim key capture cancelled (" + reason + ")");
        }

        private static void RefreshSettingLabel()
        {
            if (_setting == null || _alertOnValueChanged == null)
                return;

            try
            {
                _alertOnValueChanged.Invoke(_setting, null);
            }
            catch (Exception e)
            {
                LogError("RefreshSettingLabel: " + e.Message);
            }
        }

        private static void CreateSetting()
        {
            if (_setting != null)
                return;

            if (_settingType == null)
                _settingType = FindType("MouseSettings.SettingInt");

            if (_settingType == null)
                return;

            try
            {
                _setting = ScriptableObject.CreateInstance(_settingType);
                ((UnityEngine.Object)_setting).name = "AimKey";

                InitializeLists(_setting);
                SetField(_setting, "defaultValue", 0);
                SetField(_setting, "minValue", 0);
                SetField(_setting, "maxValue", 0);
                SetField(_setting, "incrementalValue", 1);
                SetField(_setting, "isCloudSave", false);
                SetEnumField(_setting, "displayType", "MouseSettings.SettingDisplayType", "Button");

                _alertOnValueChanged = _settingType.GetMethod("AlertOnValueChanged",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                _logger.LogInfo("[MouseAim] menu setting created (Aim key button)");
            }
            catch (Exception e)
            {
                LogError("CreateSetting: " + e);
                _setting = null;
            }
        }

        private static void ApplyPatches()
        {
            if (_patchAttempted)
                return;

            if (_pageType == null) _pageType = FindType("UI.Notebook.Settings.SettingsPageUI");
            if (_rowType == null) _rowType = FindType("UI.Notebook.Settings.SettingsListSelectableUI");
            if (_buttonType == null) _buttonType = FindType("UI.ValueChangeSelectableButton");
            if (_textType == null) _textType = FindType("ProjectLocalizedText");

            if (_pageType == null || _rowType == null || _buttonType == null)
                return;

            _patchAttempted = true;

            try
            {
                if (_harmony == null)
                    _harmony = new Harmony("com.cono.mouseaim.menu");

                MethodInfo setup = FindMethod(_rowType, "Setup", 3);
                MethodInfo updateName = FindMethod(_rowType, "UpdateName", 0);
                MethodInfo click = FindMethod(_buttonType, "OnButtonClicked", 0);
                MethodInfo refresh = FindMethod(_buttonType, "RefreshValueDisplay", 0);
                MethodInfo updateText = _textType != null
                    ? FindMethod(_textType, "UpdateText", 1)
                    : null;

                if (!_setupPatched)
                {
                    if (setup != null)
                    {
                        _harmony.Patch(setup, postfix: new HarmonyMethod(
                            typeof(MenuIntegration).GetMethod(nameof(RowSetupPostfix), BindingFlags.Static | BindingFlags.NonPublic)));
                        _setupPatched = true;
                    }
                    else
                    {
                        LogError("Setup method not found");
                    }
                }

                if (!_updateNamePatched)
                {
                    if (updateName != null)
                    {
                        _harmony.Patch(updateName, postfix: new HarmonyMethod(
                            typeof(MenuIntegration).GetMethod(nameof(RowUpdatePostfix), BindingFlags.Static | BindingFlags.NonPublic)));
                        _updateNamePatched = true;
                    }
                    else
                    {
                        LogError("UpdateName method not found");
                    }
                }

                if (!_buttonClickPatched)
                {
                    if (click != null)
                    {
                        _harmony.Patch(click, prefix: new HarmonyMethod(
                            typeof(MenuIntegration).GetMethod(nameof(OnButtonClickedPrefix), BindingFlags.Static | BindingFlags.NonPublic)));
                        _buttonClickPatched = true;
                    }
                    else
                    {
                        LogError("ValueChangeSelectableButton.OnButtonClicked not found");
                    }
                }

                if (!_buttonRefreshPatched)
                {
                    if (refresh != null)
                    {
                        _harmony.Patch(refresh, postfix: new HarmonyMethod(
                            typeof(MenuIntegration).GetMethod(nameof(ButtonRefreshPostfix), BindingFlags.Static | BindingFlags.NonPublic)));
                        _buttonRefreshPatched = true;
                    }
                    else
                    {
                        LogError("ValueChangeSelectableButton.RefreshValueDisplay not found");
                    }
                }

                if (!_textPatched && updateText != null)
                {
                    _harmony.Patch(updateText, postfix: new HarmonyMethod(
                        typeof(MenuIntegration).GetMethod(nameof(LocalizedTextPostfix), BindingFlags.Static | BindingFlags.NonPublic)));
                    _textPatched = true;
                }

                _logger.LogInfo("[MouseAim] menu Harmony patch pass complete");
            }
            catch (Exception e)
            {
                _patchAttempted = false;
                LogError("ApplyPatches: " + e.Message);
            }
        }

        private static bool OnButtonClickedPrefix(object __instance)
        {
            try
            {
                object current = GetField(__instance, "_settingInt");
                if (!ReferenceEquals(current, _setting))
                    return true;

                BeginKeyCapture();
                return false;
            }
            catch (Exception e)
            {
                LogError("OnButtonClickedPrefix: " + e.Message);
                return true;
            }
        }

        private static void ButtonRefreshPostfix(object __instance)
        {
            try
            {
                object current = GetField(__instance, "_settingInt");
                if (!ReferenceEquals(current, _setting))
                    return;

                object label = GetField(__instance, "buttonLabel");
                string text;

                if (s_isCapturing)
                {
                    text = "Press a key...";
                }
                else
                {
                    Plugin plugin = Plugin.Instance;
                    text = plugin != null ? plugin.AimButton.Value : "V";
                }

                OverrideText(label, text);
            }
            catch (Exception e)
            {
                LogError("ButtonRefreshPostfix: " + e.Message);
            }
        }

        private static void TryInject()
        {
            if (_injected || _setting == null)
                return;

            if (_pageType == null)
                _pageType = FindType("UI.Notebook.Settings.SettingsPageUI");

            if (_pageType == null)
                return;

            try
            {
                UnityEngine.Object[] pages = Resources.FindObjectsOfTypeAll(_pageType);
                if (pages == null || pages.Length == 0)
                    return;

                object fallbackPage = null;
                object fallbackGroup = null;

                for (int i = 0; i < pages.Length; i++)
                {
                    object page = pages[i];
                    if (page == null)
                        continue;

                    IList groups = GetField(page, "settingsGroups") as IList;
                    if (groups == null || groups.Count == 0)
                        continue;

                    bool isVideo = GetField(page, "settingResolution") != null;
                    object firstGroup = null;

                    for (int g = 0; g < groups.Count; g++)
                    {
                        object group = groups[g];
                        if (group == null)
                            continue;

                        IList assets = GetField(group, "settingAssets") as IList;
                        if (assets == null || assets.Count == 0)
                            continue;

                        object rect = GetField(group, "groupListRectTransform");
                        if (rect == null)
                            continue;

                        if (firstGroup == null)
                            firstGroup = group;

                        if (!isVideo)
                        {
                            if (!assets.Contains(_setting))
                                assets.Add(_setting);
                            InjectIntoPage(page);
                            return;
                        }
                    }

                    if (!isVideo && firstGroup != null)
                    {
                        IList assets = GetField(firstGroup, "settingAssets") as IList;
                        if (assets != null && !assets.Contains(_setting))
                            assets.Add(_setting);
                        InjectIntoPage(page);
                        return;
                    }

                    if (fallbackPage == null && firstGroup != null)
                    {
                        fallbackPage = page;
                        fallbackGroup = firstGroup;
                    }
                }

                if (fallbackPage != null && fallbackGroup != null)
                {
                    IList assets = GetField(fallbackGroup, "settingAssets") as IList;
                    if (assets != null && !assets.Contains(_setting))
                        assets.Add(_setting);
                    InjectIntoPage(fallbackPage);
                }
            }
            catch (Exception e)
            {
                LogError("TryInject: " + e.Message);
            }
        }

        private static void InjectIntoPage(object page)
        {
            _injected = true;

            try
            {
                object isSetupValue = GetField(page, "isSetup");
                bool isSetup = isSetupValue is bool value && value;
                if (isSetup)
                {
                    MethodInfo redraw = _pageType.GetMethod("RedrawMenu", BindingFlags.Instance | BindingFlags.Public);
                    redraw?.Invoke(page, null);
                }
            }
            catch (Exception e)
            {
                LogError("RedrawMenu: " + e.Message);
            }

            _logger.LogInfo("[MouseAim] aim key setting injected into settings page '" +
                            ((UnityEngine.Object)page).name + "'");
        }

        private static void RowSetupPostfix(object __instance, object settingAsset)
        {
            if (!ReferenceEquals(settingAsset, _setting))
            {
                ClearRowLabel(__instance);
                return;
            }

            _rowNameRow = __instance;
            OverrideRowLabel(__instance);
        }

        private static void RowUpdatePostfix(object __instance)
        {
            object current = GetRowSetting(__instance);
            if (current == null)
                return;

            if (!ReferenceEquals(current, _setting))
            {
                ClearRowLabel(__instance);
                return;
            }

            _rowNameRow = __instance;
            OverrideRowLabel(__instance);
        }

        private static object GetRowSetting(object row)
        {
            try
            {
                PropertyInfo property = row.GetType().GetProperty("SettingAsset",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                return property?.GetValue(row);
            }
            catch
            {
                return null;
            }
        }

        private static void ClearRowLabel(object row)
        {
            if (ReferenceEquals(_rowNameRow, row))
            {
                _rowNameRow = null;
                _rowNameText = null;
            }
        }

        private static void OverrideRowLabel(object row)
        {
            try
            {
                object nameText = GetField(row, "nameText");
                if (nameText == null)
                    return;

                _rowNameText = nameText;
                OverrideText(nameText, MenuLabel);
            }
            catch (Exception e)
            {
                LogError("OverrideRowLabel: " + e.Message);
            }
        }

        private static void LocalizedTextPostfix(object __instance)
        {
            if (_rowNameText == null || !ReferenceEquals(__instance, _rowNameText))
                return;

            if (_rowNameRow != null)
            {
                object current = GetRowSetting(_rowNameRow);
                if (current != null && !ReferenceEquals(current, _setting))
                {
                    _rowNameRow = null;
                    _rowNameText = null;
                    return;
                }
            }

            try
            {
                OverrideText(__instance, MenuLabel);
            }
            catch (Exception e)
            {
                LogError("LocalizedTextPostfix: " + e.Message);
            }
        }

        private static void OverrideText(object projectLocalizedText, string text)
        {
            if (projectLocalizedText == null)
                return;

            MethodInfo method = projectLocalizedText.GetType().GetMethod("OverrideText",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, new[] { typeof(string) }, null);

            method?.Invoke(projectLocalizedText, new object[] { text });
        }

        private static void InitializeLists(object setting)
        {
            Type type = setting.GetType();
            while (type != null)
            {
                FieldInfo[] fields = type.GetFields(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

                for (int i = 0; i < fields.Length; i++)
                {
                    FieldInfo field = fields[i];

                    if (field.FieldType.IsGenericType &&
                        field.FieldType.GetGenericTypeDefinition() == typeof(List<>))
                    {
                        if (field.GetValue(setting) == null)
                            field.SetValue(setting, Activator.CreateInstance(field.FieldType));
                    }
                    else if (field.Name == "_currentValidQualities" &&
                             field.GetValue(setting) == null)
                    {
                        field.SetValue(setting, new bool[16]);
                    }
                }

                type = type.BaseType;
            }
        }

        private static FieldInfo FindField(Type type, string name)
        {
            while (type != null)
            {
                FieldInfo field = type.GetField(name,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field != null)
                    return field;
                type = type.BaseType;
            }
            return null;
        }

        private static object GetField(object instance, string name)
        {
            if (instance == null)
                return null;

            FieldInfo field = FindField(instance.GetType(), name);
            return field?.GetValue(instance);
        }

        private static void SetField(object instance, string name, object value)
        {
            FieldInfo field = FindField(instance.GetType(), name);
            if (field == null)
            {
                if (_verbose && _logger != null)
                    _logger.LogWarning("[MouseAim] field not found: " + name);
                return;
            }

            field.SetValue(instance, value);
        }

        private static void SetEnumField(object instance, string fieldName, string enumTypeName, string valueName)
        {
            Type enumType = FindType(enumTypeName);
            if (enumType == null)
                return;

            object value = Enum.Parse(enumType, valueName);
            SetField(instance, fieldName, value);
        }

        private static MethodInfo FindMethod(Type type, string name, int parameterCount)
        {
            MethodInfo[] methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            for (int i = 0; i < methods.Length; i++)
            {
                if (methods[i].Name == name && methods[i].GetParameters().Length == parameterCount)
                    return methods[i];
            }
            return null;
        }

        private static Type FindType(string typeName)
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type type = assemblies[i].GetType(typeName, false);
                if (type != null)
                    return type;
            }
            return null;
        }

        private static void LogError(string message)
        {
            if (_logger != null && !_errorLogged)
            {
                _logger.LogWarning("[MouseAim] " + message);
                _errorLogged = true;
            }
        }
    }
}

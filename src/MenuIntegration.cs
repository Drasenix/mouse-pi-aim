using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace MouseAim
{
    internal static class MenuIntegration
    {
        private const string MenuLabel = "Aim key";
        private const string ActionName = "Aim";

        private static ManualLogSource _logger;
        private static bool _verbose;

        private static Type _settingType;
        private static object _setting;

        private static Type _pageType;
        private static Type _rowType;
        private static Type _remappingType;
        private static object _remappingPrefab;
        private static Type _textType;
        private static object _rowNameText;
        private static object _rowNameRow;

        private static Harmony _harmony;
        private static bool _patchAttempted;
        private static bool _setupPatched;
        private static bool _updateNamePatched;
        private static bool _textPatched;
        private static bool _addItemPatched;
        private static bool _injected;
        private static bool _bindingHooked;
        private static bool _actionErrorLogged;
        private static float _nextActionTryTime;
        private static float _nextTryTime;
        private static float _nextSettingTryTime;
        private static float _nextPatchTryTime;
        private static bool _errorLogged;

        private static InputAction _aimAction;
        private static string _lastKeyboardPath;
        private static string _lastGamepadPath;

        internal static InputAction AimAction => _aimAction;

        public static void Initialize(ManualLogSource logger, bool verbose)
        {
            _logger = logger;
            _verbose = verbose;

            EnsureInputAction();
            CreateSetting();
            ApplyPatches();
            TryInject();
        }

        public static void Tick()
        {
            EnsureInputAction();

            if (_setting == null && _aimAction != null && Time.unscaledTime >= _nextSettingTryTime)
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

        public static void Shutdown()
        {
            if (_bindingHooked)
            {
                InputManager.OnSaveBindings -= OnBindingsSaved;
                _bindingHooked = false;
            }

            if (_harmony != null)
            {
                _harmony.UnpatchSelf();
                _harmony = null;
            }
        }

        private static void EnsureInputAction()
        {
            if (_aimAction != null)
                return;

            if (Time.unscaledTime < _nextActionTryTime)
                return;

            _nextActionTryTime = Time.unscaledTime + 2f;

            try
            {
                InputManager manager = FindInputManager();
                if (manager == null)
                    return;

                DefaultInput controls = manager.Controls;
                if (controls == null)
                    return;

                InputActionAsset asset = controls.asset;
                if (asset == null)
                    return;

                InputActionMap playerMap = asset.FindActionMap("Player", false);
                if (playerMap == null)
                    return;

                InputAction action = playerMap.FindAction(ActionName, false);
                if (action == null)
                {
                    Plugin plugin = Plugin.Instance;
                    string keyboardPath = NormalizeKeyboardPath(plugin != null ? plugin.AimButton.Value : null);
                    string gamepadPath = NormalizeGamepadPath(plugin != null ? plugin.AimGamepadBinding.Value : null);

                    var enabledMaps = new List<InputActionMap>();
                    foreach (InputActionMap map in asset.actionMaps)
                    {
                        if (map.enabled)
                            enabledMaps.Add(map);
                    }

                    asset.Disable();
                    try
                    {
                        action = playerMap.AddAction(ActionName, InputActionType.Button);
                        action.AddBinding(keyboardPath, null, null, "KeyboardMouse");
                        action.AddBinding(gamepadPath, null, null, "Gamepad");
                    }
                    finally
                    {
                        for (int i = 0; i < enabledMaps.Count; i++)
                            enabledMaps[i].Enable();
                    }

                    if (_verbose)
                        _logger.LogInfo("[MouseAim] input action created: " + keyboardPath + ", " + gamepadPath);
                }

                _aimAction = action;
                _lastKeyboardPath = NormalizeKeyboardPath(GetBindingPath(action, "KeyboardMouse"));
                _lastGamepadPath = NormalizeGamepadPath(GetBindingPath(action, "Gamepad"));

                if (!_bindingHooked)
                {
                    InputManager.OnSaveBindings += OnBindingsSaved;
                    _bindingHooked = true;
                }

                _nextSettingTryTime = 0f;
            }
            catch (Exception e)
            {
                if (!_actionErrorLogged)
                {
                    _actionErrorLogged = true;
                    LogError("EnsureInputAction: " + e.Message);
                }
            }
        }

        private static InputManager FindInputManager()
        {
            try
            {
                InputManager[] managers = Resources.FindObjectsOfTypeAll<InputManager>();
                if (managers == null)
                    return null;

                for (int i = 0; i < managers.Length; i++)
                {
                    if (managers[i] != null)
                        return managers[i];
                }
            }
            catch
            {
                // ignore
            }

            return null;
        }

        private static void OnBindingsSaved()
        {
            if (_aimAction == null || Plugin.Instance == null)
                return;

            string keyboardPath = NormalizeKeyboardPath(GetBindingPath(_aimAction, "KeyboardMouse"));
            string gamepadPath = NormalizeGamepadPath(GetBindingPath(_aimAction, "Gamepad"));
            bool changed = false;

            if (!string.IsNullOrEmpty(keyboardPath) && keyboardPath != _lastKeyboardPath)
            {
                _lastKeyboardPath = keyboardPath;
                Plugin.Instance.AimButton.Value = keyboardPath;
                changed = true;
            }

            if (!string.IsNullOrEmpty(gamepadPath) && gamepadPath != _lastGamepadPath)
            {
                _lastGamepadPath = gamepadPath;
                Plugin.Instance.AimGamepadBinding.Value = gamepadPath;
                changed = true;
            }

            if (changed)
            {
                Plugin.Instance.Config.Save();

                if (_verbose)
                    _logger.LogInfo("[MouseAim] aim bindings saved: " + keyboardPath + ", " + gamepadPath);
            }
        }

        private static string GetBindingPath(InputAction action, string group)
        {
            if (action == null)
                return null;

            foreach (InputBinding binding in action.bindings)
            {
                if (binding.isPartOfComposite)
                    continue;

                string groups = binding.groups ?? string.Empty;
                if (groups.IndexOf(group, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                string path = binding.effectivePath;
                if (!string.IsNullOrEmpty(path))
                    return path;
            }

            return null;
        }

        private static string NormalizeKeyboardPath(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "<Keyboard>/v";

            string trimmed = value.Trim();
            if (trimmed.StartsWith("<", StringComparison.Ordinal))
                return trimmed;

            if (trimmed.StartsWith("/", StringComparison.Ordinal))
            {
                int slash = trimmed.IndexOf('/', 1);
                if (slash > 0)
                    return "<" + trimmed.Substring(1, slash - 1) + ">" + trimmed.Substring(slash);
            }

            if (trimmed.StartsWith("Keyboard/", StringComparison.OrdinalIgnoreCase))
                return "<Keyboard>/" + trimmed.Substring("Keyboard/".Length);

            string lower = trimmed.ToLowerInvariant();
            switch (lower)
            {
                case "middlemouse":
                case "middlebutton":
                    return "<Mouse>/middleButton";
                case "rightmouse":
                case "rightbutton":
                    return "<Mouse>/rightButton";
                case "leftmouse":
                case "leftbutton":
                    return "<Mouse>/leftButton";
                case "mouse4":
                case "mouseback":
                    return "<Mouse>/backButton";
                case "mouse5":
                case "mouseforward":
                    return "<Mouse>/forwardButton";
            }

            if (Enum.TryParse(lower, true, out Key key))
            {
                if (Keyboard.current != null)
                {
                    KeyControl control = Keyboard.current[key];
                    if (control != null)
                    {
                        string path = control.path;
                        if (path.StartsWith("/", StringComparison.Ordinal))
                        {
                            int slash = path.IndexOf('/', 1);
                            if (slash > 0)
                                return "<" + path.Substring(1, slash - 1) + ">" + path.Substring(slash);
                        }

                        return path;
                    }
                }

                return "<Keyboard>/" + lower;
            }

            return "<Keyboard>/v";
        }

        private static string NormalizeGamepadPath(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "<Gamepad>/leftShoulder";

            string trimmed = value.Trim();
            if (trimmed.StartsWith("<", StringComparison.Ordinal))
                return trimmed;

            if (trimmed.StartsWith("/", StringComparison.Ordinal))
            {
                int slash = trimmed.IndexOf('/', 1);
                if (slash > 0)
                    return "<" + trimmed.Substring(1, slash - 1) + ">" + trimmed.Substring(slash);
            }

            return "<Gamepad>/leftShoulder";
        }

        private static void CreateSetting()
        {
            if (_setting != null || _aimAction == null)
                return;

            if (_settingType == null)
                _settingType = FindType("MouseSettings.SettingInputBinding");

            if (_settingType == null)
                return;

            try
            {
                _setting = ScriptableObject.CreateInstance(_settingType);
                ((UnityEngine.Object)_setting).name = "AimKey";

                InitializeLists(_setting);

                InputActionReference reference = InputActionReference.Create(_aimAction);
                SetField(_setting, "defaultValue", reference);
                SetField(_setting, "bindingId", string.Empty);
                SetField(_setting, "isCloudSave", false);
                SetEnumField(_setting, "displayType", "MouseSettings.SettingDisplayType", "InputBinding");

                _logger.LogInfo("[MouseAim] native aim binding setting created");
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
            if (_textType == null) _textType = FindType("ProjectLocalizedText");

            if (_pageType == null || _rowType == null)
                return;

            _patchAttempted = true;

            try
            {
                if (_harmony == null)
                    _harmony = new Harmony("com.cono.mouseaim.menu");

                MethodInfo setup = FindMethod(_rowType, "Setup", 3);
                MethodInfo updateName = FindMethod(_rowType, "UpdateName", 0);
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

                if (!_textPatched && updateText != null)
                {
                    _harmony.Patch(updateText, postfix: new HarmonyMethod(
                        typeof(MenuIntegration).GetMethod(nameof(LocalizedTextPostfix), BindingFlags.Static | BindingFlags.NonPublic)));
                    _textPatched = true;
                }

                if (!_addItemPatched)
                {
                    MethodInfo addItem = FindMethod(_pageType, "AddSettingsPageItem", 4);
                    if (addItem != null)
                    {
                        _harmony.Patch(addItem,
                            prefix: new HarmonyMethod(
                                typeof(MenuIntegration).GetMethod(nameof(AddSettingsPageItemPrefix), BindingFlags.Static | BindingFlags.NonPublic)),
                            postfix: new HarmonyMethod(
                                typeof(MenuIntegration).GetMethod(nameof(AddSettingsPageItemPostfix), BindingFlags.Static | BindingFlags.NonPublic)));
                        _addItemPatched = true;
                    }
                    else
                    {
                        LogError("AddSettingsPageItem method not found");
                    }
                }

                _logger.LogInfo("[MouseAim] menu Harmony patch pass complete");
            }
            catch (Exception e)
            {
                _patchAttempted = false;
                LogError("ApplyPatches: " + e.Message);
            }
        }

        private static void AddSettingsPageItemPrefix(object __instance, object item, ref object __state)
        {
            __state = null;

            if (!ReferenceEquals(item, _setting) || _remappingPrefab == null)
                return;

            object current = GetField(__instance, "listItemPrefab");
            if (current == null)
                return;

            if (_remappingType != null && _remappingType.IsAssignableFrom(current.GetType()))
                return;

            __state = current;
            SetField(__instance, "listItemPrefab", _remappingPrefab);

            if (_verbose)
                _logger.LogInfo("[MouseAim] using remapping row prefab for Aim key");
        }

        private static void AddSettingsPageItemPostfix(object __instance, object __state)
        {
            if (__state != null)
                SetField(__instance, "listItemPrefab", __state);
        }

        private static void TryInject()
        {
            if (_injected || _setting == null)
                return;

            if (_pageType == null)
                _pageType = FindType("UI.Notebook.Settings.SettingsPageUI");

            if (_remappingType == null)
                _remappingType = FindType("UI.Notebook.Settings.SettingsListRemappingSelectableUI");

            if (_pageType == null)
                return;

            try
            {
                UnityEngine.Object[] pages = Resources.FindObjectsOfTypeAll(_pageType);
                if (pages == null || pages.Length == 0)
                    return;

                object exactPage = null;
                object exactGroup = null;
                object doubleTapPage = null;
                object doubleTapGroup = null;
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

                    object pagePrefab = GetField(page, "listItemPrefab");
                    if (_remappingPrefab == null && _remappingType != null && pagePrefab != null &&
                        _remappingType.IsAssignableFrom(pagePrefab.GetType()))
                    {
                        _remappingPrefab = pagePrefab;
                    }

                    bool isVideo = GetField(page, "settingResolution") != null;
                    if (isVideo)
                        continue;

                    object pageFirstGroup = null;
                    object pageDoubleTapGroup = null;

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

                        if (pageFirstGroup == null)
                            pageFirstGroup = group;

                        if (pageDoubleTapGroup == null &&
                            ContainsSettingName(assets, "DoubleTapDashEnabled"))
                        {
                            pageDoubleTapGroup = group;
                        }
                    }

                    if (pageFirstGroup == null)
                        continue;

                    string pageName = ((UnityEngine.Object)page).name;

                    if (pageName == "Page_Controls" && exactPage == null)
                    {
                        exactPage = page;
                        exactGroup = pageDoubleTapGroup ?? pageFirstGroup;
                    }

                    if (pageDoubleTapGroup != null && doubleTapPage == null)
                    {
                        doubleTapPage = page;
                        doubleTapGroup = pageDoubleTapGroup;
                    }

                    if (fallbackPage == null)
                    {
                        fallbackPage = page;
                        fallbackGroup = pageDoubleTapGroup ?? pageFirstGroup;
                    }
                }

                object targetPage = exactPage ?? doubleTapPage ?? fallbackPage;
                object targetGroup = exactPage != null ? exactGroup :
                                     doubleTapPage != null ? doubleTapGroup : fallbackGroup;

                if (targetPage == null || targetGroup == null)
                    return;

                if (_verbose)
                {
                    object targetPrefab = GetField(targetPage, "listItemPrefab");
                    _logger.LogInfo("[MouseAim] target page '" + ((UnityEngine.Object)targetPage).name +
                                    "' rowPrefab=" + (targetPrefab != null ? targetPrefab.GetType().FullName : "null") +
                                    " remappingPrefab=" + (_remappingPrefab != null ? "yes" : "no"));
                }

                if (_remappingPrefab == null)
                {
                    _logger.LogWarning("[MouseAim] remapping row prefab not found; Aim key may not open the native rebind popup");
                }

                IList targetAssets = GetField(targetGroup, "settingAssets") as IList;
                if (targetAssets != null && !targetAssets.Contains(_setting))
                    targetAssets.Add(_setting);

                InjectIntoPage(targetPage);
            }
            catch (Exception e)
            {
                LogError("TryInject: " + e.Message);
            }
        }

        private static bool ContainsSettingName(IList assets, string name)
        {
            if (assets == null)
                return false;

            for (int i = 0; i < assets.Count; i++)
            {
                UnityEngine.Object asset = assets[i] as UnityEngine.Object;
                if (asset != null && asset.name == name)
                    return true;
            }

            return false;
        }

        private static void InjectIntoPage(object page)
        {
            _injected = true;

            try
            {
                object prefab = GetField(page, "listItemPrefab");
                if (_verbose)
                {
                    _logger.LogInfo("[MouseAim] page '" + ((UnityEngine.Object)page).name +
                                    "' rowPrefab=" + (prefab != null ? prefab.GetType().FullName : "null"));
                }

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

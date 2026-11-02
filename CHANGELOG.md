# Changelog

## 1.3.1
- Fixed the **Aim key** row not appearing on the main Controls/Commands page.
  It is now injected on `Page_Controls`, under the Double Tap row.
- When that page uses the standard settings row, the mod swaps in the native
  `SettingsListRemappingSelectableUI` prefab only for the Aim key row, so the
  native rebinding popup still works.

## 1.3.0
- The **Aim key** row now uses the game's native Commands rebinding UI
  (`SettingInputBinding` / `ControlsRemapping`): click it, then press the key
  or mouse button in the same popup style as the other Commands entries.
- Removed the custom key-capture row and its dead code.
- Added `AimGamepadBinding` for the gamepad side of the native rebinding.
- Default key remains **V**.
- No weapon animation or weapon pose change yet.
- No sensitivity or recoil change.

## 1.2.0
- Aim key can now be captured directly: click **Settings > Controls > Aim key**,
  then press the keyboard key or mouse button you want.
- Removed the fixed key list; any keyboard key or mouse button is accepted.
- Default key remains **V**.
- No weapon animation or weapon pose change yet.
- No sensitivity or recoil change.

## 1.1.0
- Default aim key changed from middle mouse to **V**.
- Added an **Aim key** row in **Settings > Controls**. It cycles through V,
  MiddleMouse, RightMouse, LeftShift, Mouse4, Mouse5, B and N, and writes the
  choice to the BepInEx config.
- Increased default zoom from 20° to 28°.
- Made the zoom start faster: shorter transition (0.08 s) and exponential
  smoothing instead of SmoothDamp.
- Aim controller now runs before the camera update.
- Verbose logs now include the aim start/stop reason, key, zoom and spread.
- No weapon animation or weapon pose change yet.
- No sensitivity or recoil change.

## 1.0.0
- Initial release.
- Hold the dedicated aim button (middle mouse by default) to zoom the camera.
- Reduced weapon spread while aiming.
- Crosshair tightens automatically through the game's spread calculation.
- Aim is disabled in menus, pause, photo mode, while holstered/changing, and
  while a secondary ability is active.
- No weapon animation or weapon pose change yet.

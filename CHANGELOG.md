# Changelog

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

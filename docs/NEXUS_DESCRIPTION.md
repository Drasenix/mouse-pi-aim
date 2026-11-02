# Mouse Aim

Hold a dedicated key to aim in MOUSE: P.I. For Hire. The camera zooms in and
weapon spread is reduced while aiming.

- Default aim button: **V** (hold to aim, release to stop).
- Camera FOV zoom with a smooth, responsive transition.
- Reduced weapon spread; the crosshair tightens automatically.
- Dedicated, configurable aim button: `V`, `MiddleMouse`, `RightMouse`,
  `LeftShift`, `Mouse4`, `Mouse5`, `B` or `N`.
- The aim key can be changed in **Settings > Controls > Aim key**.
- Does not use the game's `AlternateShoot` action, so it does not conflict with
  secondary fire or native weapon scopes.
- Works alongside **Mouse Double Tap Dash**.

## Version 1.1.0

- Default key changed from middle mouse to **V**.
- In-game **Aim key** setting added.
- Default zoom increased to 28°.
- Faster, more responsive zoom transition.
- No weapon animation or weapon pose change yet: the weapon stays in its normal
  pose while aiming.
- No sensitivity or recoil change.
- Generic aim is skipped while a weapon's secondary ability is active (native
  scope or alternate fire), to avoid stacking two zoom effects.

## Requirements

- **MOUSE: P.I. For Hire** (tested with the 14/10/2026 build).
- **BepInEx 5.4.23.5 x64**, with the game-specific **unstripped** setup for
  MOUSE: P.I. For Hire.
  - Nexus: **Unstripped BepInEx for M.P.I.**
  - Do not use a stock BepInEx package: the game ships stripped BCL assemblies
    and the preloader will fail.
- Single-player. No anti-cheat is involved.

## Installation

1. Install the unstripped BepInEx package into the game root (where `MOUSE.exe`
   is).
2. Extract this archive into the game root:

   ```
   <game>/BepInEx/plugins/MouseAim.dll
   ```

3. Launch the game.
4. Hold **V** while a weapon is drawn.

## Configuration

File: `BepInEx/config/com.cono.mouseaim.cfg`

```ini
[General]
Enabled = true
AimButton = V

[Zoom]
AimZoomDegrees = 28
AimTransitionSeconds = 0.08

[Accuracy]
AimSpreadMultiplier = 0.5

[Conditions]
RequireWeaponDrawn = true

[Debug]
Verbose = false
```

- `AimZoomDegrees = 0`: accuracy only, no zoom.
- `AimSpreadMultiplier = 1`: zoom only, no accuracy change.
- `AimButton`: `V`, `MiddleMouse`, `RightMouse`, `LeftShift`, `Mouse4`,
  `Mouse5`, `B` or `N`. Can also be changed in **Settings > Controls > Aim key**.
  `RightMouse` may conflict with secondary fire or native scopes.

Change the values in the file and restart the game.

## Notes

- If the menu entry or zoom is missing after a game update, check
  `BepInEx/LogOutput.log` for `MouseAim`.
- Linux / Steam Proton / Lutris: install the Windows BepInEx package in the
  game folder. No special launch options are required.

## Credits

- BepInEx Team and HarmonyX.
- KZekai and the authors of the unstripped BepInEx setup for
  MOUSE: P.I. For Hire.
- Fumi Games for the game.

## Permissions

Plugin code is released under the MIT license. Do not redistribute the game's
assemblies or the BepInEx unstripped package without following their respective
licenses.

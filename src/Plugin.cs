using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;

namespace MouseAim
{
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.cono.mouseaim";
        public const string Name = "Mouse Aim";
        public const string Version = "1.0.0";

        internal static Plugin Instance { get; private set; }

        internal ManualLogSource Log => Logger;

        internal ConfigEntry<bool> Enabled;
        internal ConfigEntry<string> AimButton;
        internal ConfigEntry<float> AimZoomDegrees;
        internal ConfigEntry<float> AimTransitionSeconds;
        internal ConfigEntry<float> AimSpreadMultiplier;
        internal ConfigEntry<bool> RequireWeaponDrawn;
        internal ConfigEntry<bool> Verbose;

        private void Awake()
        {
            Instance = this;

            Enabled = Config.Bind("General", "Enabled", true,
                "Active la visée maintenue.");
            AimButton = Config.Bind("General", "AimButton", "MiddleMouse",
                "Touche de visée maintenue. Ex: MiddleMouse, RightMouse, Mouse4, Mouse5, LeftShift, V. " +
                "Le clic molette est dédié et n'utilise pas l'action AlternateShoot du jeu.");

            AimZoomDegrees = Config.Bind("Zoom", "AimZoomDegrees", 20f,
                new ConfigDescription("Réduction du FOV pendant la visée, en degrés.",
                    new AcceptableValueRange<float>(0f, 60f)));
            AimTransitionSeconds = Config.Bind("Zoom", "AimTransitionSeconds", 0.15f,
                new ConfigDescription("Durée de la transition de zoom, en secondes.",
                    new AcceptableValueRange<float>(0.01f, 1f)));

            AimSpreadMultiplier = Config.Bind("Accuracy", "AimSpreadMultiplier", 0.5f,
                new ConfigDescription("Multiplicateur du spread pendant la visée. 0.5 = spread divisé par deux.",
                    new AcceptableValueRange<float>(0f, 1f)));

            RequireWeaponDrawn = Config.Bind("Conditions", "RequireWeaponDrawn", true,
                "N'autorise la visée que si une arme est sortie.");
            Verbose = Config.Bind("Debug", "Verbose", false,
                "Écrit un log à chaque entrée/sortie de visée.");

            gameObject.AddComponent<AimController>();

            Logger.LogInfo("[MouseAim] loaded v" + Version);
        }
    }
}

using System;
using MelonLoader;

[assembly: MelonInfo(typeof(NextGenHardware.NextGenHardwareMod), "Next-Gen Hardware", "1.1.0", "checkersx")]
[assembly: MelonGame("Waseku", "Data Center")]

namespace NextGenHardware
{
    // Adds Gen 2/3/4 versions of every vanilla server (same model, more IOPS, faster ports, front light
    // bars) and a starter switch with 4 extra SFP ports. Saves stay vanilla on disk (see SaveCompat).
    // The mod was called "New Servers" until v0.5; save-facing IDs keep that name.
    public class NextGenHardwareMod : MelonMod
    {
        internal static MelonLogger.Instance Log;

        public override void OnInitializeMelon()
        {
            Log = LoggerInstance;
            ServiceRequestIops.Init();
            Patching.Install(HarmonyInstance);
        }

        // Covers a new game. A loaded game also gets a call from SaveCompat before servers spawn.
        public override void OnSceneWasLoaded(int buildIndex, string sceneName) => Setup($"scene '{sceneName}' loaded");

        public override void OnSceneWasInitialized(int buildIndex, string sceneName) => Setup($"scene '{sceneName}' initialized");

        public override void OnUpdate()
        {
            SaveCompat.RestoreIfStuck();
            Lights.Animate();
        }

        internal static void Setup(string reason)
        {
            try
            {
                if (!Registry.EnsureRegistered(reason)) return;
                Switches.EnsureRegistered(); // optional: the servers still work if this fails
                ShopCards.EnsureCards();
            }
            catch (Exception e)
            {
                Log.Error($"Setup failed ({reason}): {e}");
            }
        }
    }
}

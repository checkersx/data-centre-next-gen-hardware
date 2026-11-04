using System;
using HarmonyLib;
using Il2Cpp;

namespace NextGenHardware
{
    internal static class Patching
    {
        public static void Install(HarmonyLib.Harmony h)
        {
            Patch(h, typeof(SaveSystem), nameof(SaveSystem.SaveGame), typeof(SaveCompat), nameof(SaveCompat.SaveGamePrefix), nameof(SaveCompat.SaveGamePostfix));
            Patch(h, typeof(SaveSystem), nameof(SaveSystem.WriteMeta), typeof(SaveCompat), nameof(SaveCompat.WriteMetaPrefix), null);
            Patch(h, typeof(SaveSystem), nameof(SaveSystem.LoadGame), typeof(SaveCompat), null, nameof(SaveCompat.LoadGamePostfix));
            Patch(h, typeof(SaveSystem), nameof(SaveSystem.MoveSaveFiles), typeof(SaveCompat), null, nameof(SaveCompat.MoveSaveFilesPostfix));
            Patch(h, typeof(SaveSystem), nameof(SaveSystem.DeleteSaveFile), typeof(SaveCompat), null, nameof(SaveCompat.DeleteSaveFilePostfix));
            Patch(h, typeof(SaveSystem), nameof(SaveSystem.DeleteSaveFilesQuiet), typeof(SaveCompat), null, nameof(SaveCompat.DeleteSaveFilePostfix));

            Patch(h, typeof(Server), nameof(Server.Start), typeof(Registry), null, nameof(Registry.ServerPostfix));
            Patch(h, typeof(Server), nameof(Server.ServerInsertedInRack), typeof(Registry), null, nameof(Registry.ServerPostfix));

            foreach (var method in new[]
            {
                nameof(Server.Start), nameof(Server.ServerInsertedInRack), nameof(Server.OnLoadingComplete), nameof(Server.PowerButton),
                nameof(Server.TurnOnCommonFunction), nameof(Server.TurnOffCommonFunctions), nameof(Server.ItIsBroken),
                nameof(Server.RepairDevice), nameof(Server.ForceShutdownByAttack),
            })
                Patch(h, typeof(Server), method, typeof(Lights), null, nameof(Lights.SyncPostfix));

            Patch(h, typeof(CableLink), nameof(CableLink.InsertSFP), typeof(Ports), null, nameof(Ports.CageBackPostfix));
            Patch(h, typeof(CableLink), nameof(CableLink.RemoveSFP), typeof(Ports), null, nameof(Ports.CageBackPostfix));
            Patch(h, typeof(CableLink), nameof(CableLink.InteractOnHover), typeof(Ports), null, nameof(Ports.OutlinePostfix));

            Patch(h, typeof(ShopItem), nameof(ShopItem.Start), typeof(ShopCards), null, nameof(ShopCards.RefreshTextPostfix));
            Patch(h, typeof(ShopItem), nameof(ShopItem.UpdateVisualState), typeof(ShopCards), null, nameof(ShopCards.RefreshTextPostfix));
            Patch(h, typeof(ShopItem), nameof(ShopItem.ButtonBuyItem), typeof(ShopCards), nameof(ShopCards.UnlockGatePrefix), null);
            Patch(h, typeof(ShopItem), nameof(ShopItem.TryUnlock), typeof(ShopCards), nameof(ShopCards.UnlockGatePrefix), null);

            Patch(h, typeof(CreateSubnetAndMoveServersSR), nameof(CreateSubnetAndMoveServersSR.IsCompleted), typeof(ServiceRequestIops), nameof(ServiceRequestIops.IsCompletedPrefix), null);
            Patch(h, typeof(CreateSubnetAndMoveServersSR), nameof(CreateSubnetAndMoveServersSR.GetProgressText), typeof(ServiceRequestIops), null, nameof(ServiceRequestIops.ProgressPostfix));
            Patch(h, typeof(ServiceRequestRow), nameof(ServiceRequestRow.OnClickServiceRequestRow), typeof(ServiceRequestIops), null, nameof(ServiceRequestIops.DescriptionPostfix));

            Patch(h, typeof(StaticUIElements), nameof(StaticUIElements.AddMeesageInField), typeof(MessageQueueGuard), nameof(MessageQueueGuard.Prefix), null);
            Patch(h, typeof(StaticUIElements), nameof(StaticUIElements.UpdateMessageDisplay), typeof(MessageQueueGuard), nameof(MessageQueueGuard.Prefix), null);
        }

        static void Patch(HarmonyLib.Harmony h, Type type, string method, Type patchType, string prefix, string postfix)
        {
            try
            {
                var target = AccessTools.Method(type, method);
                if (target == null)
                {
                    NextGenHardwareMod.Log.Error($"Patch target {type.Name}.{method} not found; the game may have changed.");
                    return;
                }
                h.Patch(target,
                    prefix: prefix == null ? null : new HarmonyMethod(AccessTools.Method(patchType, prefix)),
                    postfix: postfix == null ? null : new HarmonyMethod(AccessTools.Method(patchType, postfix)));
            }
            catch (Exception e)
            {
                NextGenHardwareMod.Log.Error($"Patching {type.Name}.{method} failed: {e.Message}");
            }
        }
    }
}

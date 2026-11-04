using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Il2Cpp;
using MelonLoader;

namespace NextGenHardware
{
    // "New subnet" service requests (CreateSubnetAndMoveServersSR) ask for N servers in a new subnet.
    // With upgraded servers that means racking cheap filler servers just to make up the count, so the
    // request is measured in IOPS instead: N × 5,000 IOPS (N vanilla 3U servers' worth) of rated
    // capacity. Everything else follows vanilla: the subnet is the router subnet routed to the
    // customer's app, and a server counts if it is set to that customer and has an IP in the subnet,
    // whether or not it is switched on.
    //
    // Only the check and the displayed text change. The SR keeps its vanilla server count, so saves
    // are unchanged. Every server has at least 5,000 IOPS, so anything vanilla accepts is accepted here.
    // Without the mod, a request met on IOPS with fewer servers goes back to In Progress (the game
    // re-checks resolved requests, and rewardGranted stops a second payout).
    internal static class ServiceRequestIops
    {
        const int IopsPerServer = 5000;
        const float Unit = 100000f;   // maxProcessingSpeed is IOPS ÷ 100,000
        const int LongDescriptionId = 303; // "{0} requires a dedicated subnet for app {1}. … 3) Move {2} servers into the new subnet."

        static readonly Regex ServersLine = new Regex(@"<color=(?:red|green)>\[(?:  |OK)\]</color> Servers in subnet: \d+ / \d+");
        static readonly Dictionary<string, HashSet<string>> UsableIps = new Dictionary<string, HashSet<string>>();
        static MelonPreferences_Entry<bool> _enabled;
        static bool _loggedError;

        static bool Enabled => _enabled == null || _enabled.Value;

        internal static void Init()
        {
            var category = MelonPreferences.CreateCategory("NextGenHardware", "Next-Gen Hardware");
            _enabled = category.CreateEntry("ServiceRequestsCountIops", true, "Service requests count IOPS",
                "New-subnet service requests ask for IOPS (5,000 per server the request names) instead of a number of servers.");
            NextGenHardwareMod.Log.Msg($"New-subnet service requests count {(Enabled ? "IOPS (5,000 per requested server)" : "servers (vanilla)")}.");
        }

        // Prefix on CreateSubnetAndMoveServersSR.IsCompleted: replaces the server count with an IOPS total.
        public static bool IsCompletedPrefix(CreateSubnetAndMoveServersSR __instance, ref bool __result)
        {
            if (!Enabled) return true;
            try
            {
                __result = TryMeasure(__instance, out long iops) && iops >= Target(__instance);
                return false;
            }
            catch (Exception e)
            {
                LogOnce($"IOPS check failed, using the vanilla server count: {e}");
                return true;
            }
        }

        // Postfix on CreateSubnetAndMoveServersSR.GetProgressText: "Servers in subnet: n / x" becomes an IOPS line.
        public static void ProgressPostfix(CreateSubnetAndMoveServersSR __instance, ref string __result)
        {
            if (!Enabled || string.IsNullOrEmpty(__result)) return;
            try
            {
                var m = ServersLine.Match(__result);
                if (!m.Success) return;
                TryMeasure(__instance, out long iops);
                long target = Target(__instance);
                string mark = iops >= target ? "<color=green>[OK]</color>" : "<color=red>[  ]</color>";
                __result = __result.Substring(0, m.Index) + $"{mark} IOPS in subnet: {Num(iops)} / {Num(target)}" +
                           __result.Substring(m.Index + m.Length);
            }
            catch (Exception e) { LogOnce($"Progress text: {e}"); }
        }

        // Postfix on ServiceRequestRow.OnClickServiceRequestRow: rewrites the "Move N servers" step in the
        // side panel. Done at display time so the SR's stored description (and the save) stay vanilla.
        public static void DescriptionPostfix(ServiceRequestRow __instance)
        {
            if (!Enabled) return;
            try
            {
                var sr = __instance.boundSR?.TryCast<CreateSubnetAndMoveServersSR>();
                var panel = (__instance.serviceRequests ?? ServiceRequests.instance)?.textsrLongDescription;
                if (sr == null || panel == null) return;
                var rewritten = RewriteDescription(panel.text, sr);
                if (rewritten != null) panel.text = rewritten;
            }
            catch (Exception e) { LogOnce($"Description text: {e}"); }
        }

        static long Target(CreateSubnetAndMoveServersSR sr) => (long)sr.requiredServerCount * IopsPerServer;

        // Mirrors vanilla IsCompleted (build 25869593), summing rated IOPS instead of counting servers.
        // False when the new subnet doesn't exist yet: no router subnet is routed to the customer's app.
        static bool TryMeasure(CreateSubnetAndMoveServersSR sr, out long iops)
        {
            iops = 0;
            var map = NetworkMap.instance;
            var setIp = MainGameManager.instance?.setIP;
            if (map == null || setIp == null || map.switches == null || map.servers == null) return false;

            var cb = map.GetCustomerBase(sr.targetCustomerID);
            if (cb == null || cb.vlanIdsPerApp == null || !cb.vlanIdsPerApp.TryGetValue(sr.targetAppID, out int appVlan)) return false;

            // The first route (in the map's order) that points straight at the app's VLAN from a subnet the router owns.
            string subnet = null;
            foreach (var kv in map.switches)
            {
                var router = kv.Value?.TryCast<Router>();
                var routes = router?.routes;
                if (routes == null) continue;
                for (int i = 0; i < routes.Count && subnet == null; i++)
                {
                    var route = routes[i];
                    if (route == null || !string.IsNullOrEmpty(route.targetIp) || route.targetVlanId != appVlan || route.sourceVlanId <= 0) continue;
                    var cidr = router.GetSubnet(route.sourceVlanId)?.subnetCidr;
                    if (!string.IsNullOrEmpty(cidr)) subnet = cidr;
                }
                if (subnet != null) break;
            }
            if (subnet == null) return false;

            var usable = Usable(setIp, subnet);
            if (usable.Count == 0) return false;

            foreach (var kv in map.servers)
            {
                var server = kv.Value?.TryCast<Server>();
                if (server == null || server.GetCustomerID() != sr.targetCustomerID) continue;
                var ip = server.IP;
                if (string.IsNullOrEmpty(ip) || !usable.Contains(ip)) continue;
                iops += (long)Math.Round(server.maxProcessingSpeed * Unit);
            }
            return true;
        }

        // GetUsableIPsFromSubnet depends only on the CIDR string, and the completion check polls every few seconds.
        static HashSet<string> Usable(SetIP setIp, string subnet)
        {
            if (UsableIps.TryGetValue(subnet, out var set)) return set;
            set = new HashSet<string>();
            var ips = setIp.GetUsableIPsFromSubnet(subnet);
            if (ips != null)
                for (int i = 0; i < ips.Length; i++)
                    if (!string.IsNullOrEmpty(ips[i])) set.Add(ips[i]);
            if (set.Count > 0) UsableIps[subnet] = set;
            return set;
        }

        // Finds the template's "{2}" step as the game formatted it ("3) Move 20 servers into the new subnet.")
        // and replaces it. English gets a reworded sentence; other languages get "{2} IOPS" in place of the count.
        static string RewriteDescription(string text, CreateSubnetAndMoveServersSR sr)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var template = Localisation.instance?.ReturnTextByID(LongDescriptionId);
            if (string.IsNullOrEmpty(template)) return null;

            foreach (var line in template.Replace("\\n", "\n").Split('\n'))
            {
                if (!line.Contains("{2}") || line.Contains("{0}") || line.Contains("{1}")) continue;
                string shown = line.Replace("{2}", sr.requiredServerCount.ToString());
                if (!text.Contains(shown)) return null;
                string replacement = line.Contains("Move {2} servers")
                    ? line.Replace("Move {2} servers", "Move servers with a total of {2} IOPS")
                    : line.Replace("{2}", "{2} IOPS");
                return text.Replace(shown, replacement.Replace("{2}", Num(Target(sr))));
            }
            return null;
        }

        static string Num(long n) => n.ToString("N0", CultureInfo.InvariantCulture);

        static void LogOnce(string message)
        {
            if (_loggedError) return;
            _loggedError = true;
            NextGenHardwareMod.Log.Error($"Service request IOPS: {message}");
        }
    }
}

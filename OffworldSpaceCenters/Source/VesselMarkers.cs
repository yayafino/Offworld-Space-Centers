using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using KSP.UI.Screens;
using UnityEngine;

namespace OffworldSpaceCenters
{
    /// <summary>
    /// The Space Center scene puts a "Fly / Recover" marker over every vessel landed on the home world
    /// (KSCVesselMarkers.SpawnVesselMarkers). In an off-world Space Center scene those vessels are on another
    /// planet, so their markers end up anchored millions of metres away, outside the view frustum, and the
    /// dialog's Start() runs before the main camera is up. Here the stock spawn is replaced, in that scene only,
    /// with markers for the vessels sitting at the base itself: same prefab, same buttons, same dismiss handler.
    /// </summary>
    public static class MarkerPatches
    {
        private static readonly FieldInfo markersField = AccessTools.Field(typeof(KSCVesselMarkers), "markers");
        private static readonly MethodInfo onDismissMethod = AccessTools.Method(typeof(KSCVesselMarkers), "OnMarkerDismiss");
        private static Coroutine pending;

        public static bool ReflectionOk { get { return markersField != null && onDismissMethod != null; } }

        // KSCVesselMarkers.SpawnVesselMarkers() prefix
        public static bool SpawnVesselMarkers_Prefix(KSCVesselMarkers __instance)
        {
            try
            {
                if (!NativeSpaceCenter.Active || !ReflectionOk) return true;
                if (pending != null) __instance.StopCoroutine(pending);
                pending = __instance.StartCoroutine(SpawnAtBase(__instance));
                return false;
            }
            catch (Exception e)
            {
                Log.Warn("Vessel markers prefix: " + e.Message);
                return true;
            }
        }

        private static IEnumerator SpawnAtBase(KSCVesselMarkers km)
        {
            // AnchoredDialog.Start caches Camera.main; make sure the scene camera exists before creating any marker
            int guard = 0;
            while (Camera.main == null && guard++ < 900) yield return null;
            pending = null;
            if (km == null || Camera.main == null || !NativeSpaceCenter.Active) yield break;
            OffworldCenter center = NativeSpaceCenter.Center;
            CelestialBody body = NativeSpaceCenter.Body;
            if (center == null || body == null) yield break;

            List<KSCVesselMarker> markers = markersField.GetValue(km) as List<KSCVesselMarker>;
            if (markers == null) yield break;
            Callback<Vessel, KSCVesselMarker.DismissAction> onDismiss;
            try
            {
                onDismiss = (Callback<Vessel, KSCVesselMarker.DismissAction>)Delegate.CreateDelegate(
                    typeof(Callback<Vessel, KSCVesselMarker.DismissAction>), km, onDismissMethod);
            }
            catch (Exception e)
            {
                Log.Warn("Vessel markers: cannot bind the stock dismiss handler: " + e.Message);
                yield break;
            }

            double range = Math.Max(center.RadiusMeters, 5000d);
            int made = 0;
            List<Vessel> vessels = FlightGlobals.Vessels;
            for (int i = 0; i < vessels.Count; i++)
            {
                Vessel v = vessels[i];
                try
                {
                    if (v == null || v.mainBody != body || !v.LandedOrSplashed) continue;
                    if (v.vesselType == VesselType.DeployedSciencePart || v.vesselType == VesselType.DroppedPart) continue;
                    if (GhostSpaceCenter.IsGhost(v) || center.IsAnchor(v)) continue;
                    if (center.DistanceTo(v) > range) continue;
                    KSCVesselMarker m = KSCVesselMarker.Create(v, onDismiss);
                    if (m != null)
                    {
                        markers.Add(m);
                        made++;
                    }
                }
                catch (Exception e)
                {
                    Log.Warn("Vessel marker for '" + (v != null ? v.vesselName : "?") + "' failed: " + e.Message);
                }
            }
            if (made > 0) Log.Info(made + " vessel marker(s) placed at " + center.Name);
        }
    }
}

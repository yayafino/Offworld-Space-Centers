using System;
using System.Collections;
using UnityEngine;

namespace KolonySpaceCenters
{
    /// <summary>
    /// Watches the active vessel in flight. When a landed vessel crosses a flagged base's radius
    /// (e.g. a rover driving in), it nudges the stock altimeter buttons to re-evaluate and posts a message.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class FlightMonitor : MonoBehaviour
    {
        public static FlightMonitor Instance { get; private set; }

        /// <summary>Center the active vessel is currently inside, or null.</summary>
        public KolonyCenter CurrentCenter { get; private set; }
        public double CurrentDistance { get; private set; }
        /// <summary>Nearest active center on the current body regardless of range.</summary>
        public KolonyCenter NearestCenter { get; private set; }
        public double NearestDistance { get; private set; }

        private bool lastInside;
        private Vessel lastVessel;

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            GameEvents.onVesselChange.Add(OnVesselChange);
            GameEvents.onFlightReady.Add(OnFlightReady);
            StartCoroutine(Poll());
        }

        private void OnDestroy()
        {
            GameEvents.onVesselChange.Remove(OnVesselChange);
            GameEvents.onFlightReady.Remove(OnFlightReady);
            if (Instance == this) Instance = null;
        }

        private void OnFlightReady()
        {
            if (!string.IsNullOrEmpty(SceneActions.PendingBaseViewName))
            {
                string name = SceneActions.PendingBaseViewName;
                SceneActions.PendingBaseViewName = "";
                if (KolonySettings.AutoOpenPanel && KolonyWindow.Instance != null)
                {
                    KolonyWindow.Instance.ShowBasePanel();
                }
                ScreenMessages.PostScreenMessage("Welcome to " + name, 4f, ScreenMessageStyle.UPPER_CENTER);
            }
        }

        private void OnVesselChange(Vessel v)
        {
            lastInside = false;
            lastVessel = null;
            Evaluate(true);
        }

        private IEnumerator Poll()
        {
            while (true)
            {
                yield return new WaitForSeconds(0.5f);
                Evaluate(false);
            }
        }

        private void Evaluate(bool silent)
        {
            try
            {
                Vessel v = FlightGlobals.ActiveVessel;
                KolonyScenario sc = KolonyScenario.Instance;
                if (v == null || sc == null)
                {
                    CurrentCenter = null;
                    NearestCenter = null;
                    return;
                }

                foreach (KolonyCenter vc in sc.Centers) if (vc.IsVessel) vc.SyncFromAnchor();

                double nd;
                NearestCenter = sc.Nearest(v, out nd);
                NearestDistance = nd;

                KolonyCenter inside;
                sc.IsRecoverableAtCenter(v, out inside);
                CurrentDistance = inside != null ? inside.DistanceTo(v) : double.PositiveInfinity;
                CurrentCenter = inside;
                double d = CurrentDistance;

                bool isInside = inside != null;
                bool changed = (isInside != lastInside) || (v != lastVessel);
                lastInside = isInside;
                lastVessel = v;

                if (changed)
                {
                    // Stock only handles "landed on the home world"; everything else needs a nudge.
                    if (v.mainBody != null && !(v.mainBody.isHomeWorld && v.LandedOrSplashed))
                    {
                        AltimeterPatches.Refresh(v);
                    }
                    if (!silent && KolonySettings.RangeMessages && KolonySettings.RecoveryEnabled && !GhostSpaceCenter.IsGhost(v))
                    {
                        if (isInside)
                        {
                            ScreenMessages.PostScreenMessage(inside.Name + ": inside recovery zone (" + FormatDistance(d) + ")", 4f, ScreenMessageStyle.UPPER_LEFT);
                        }
                        else if (NearestCenter != null && (v.LandedOrSplashed || NearestCenter.IsVessel))
                        {
                            ScreenMessages.PostScreenMessage("Left " + NearestCenter.Name + " recovery zone", 3f, ScreenMessageStyle.UPPER_LEFT);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Log.Warn("FlightMonitor: " + e.Message);
            }
        }

        public static string FormatDistance(double meters)
        {
            if (double.IsInfinity(meters) || double.IsNaN(meters)) return "n/a";
            if (meters < 1000d) return meters.ToString("N0") + " m";
            return (meters / 1000d).ToString("N1") + " km";
        }
    }
}

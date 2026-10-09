using System;
using System.Collections.Generic;
using UnityEngine;

namespace KolonySpaceCenters
{
    /// <summary>Scene transitions used by the base panel: editors, tracking station, recovery, base view.</summary>
    public static class SceneActions
    {
        /// <summary>Set when "Open base view" was used so the panel can open itself on arrival.</summary>
        public static string PendingBaseViewName = "";

        /// <summary>Facility to open as soon as the real Space Center scene has loaded.</summary>
        public static FacilityRole PendingFacility = FacilityRole.None;

        public static bool CanLeaveFlight(out string reason)
        {
            reason = "";
            if (HighLogic.LoadedScene != GameScenes.FLIGHT) return true;
            ClearToSaveStatus status = FlightGlobals.ClearToSave();
            if (status != ClearToSaveStatus.CLEAR)
            {
                reason = FlightGlobals.GetNotClearToSaveStatusReason(status, "");
                return false;
            }
            return true;
        }

        private static bool SaveGame()
        {
            try
            {
                GamePersistence.SaveGame("persistent", HighLogic.SaveFolder, SaveMode.OVERWRITE);
                return true;
            }
            catch (Exception e)
            {
                Log.Error("Save failed: " + e.Message);
                ScreenMessages.PostScreenMessage("Could not save the game: " + e.Message, 5f, ScreenMessageStyle.UPPER_CENTER);
                return false;
            }
        }

        public static void OpenEditor(KolonyCenter center, EditorFacility facility)
        {
            string reason;
            if (!CanLeaveFlight(out reason))
            {
                ScreenMessages.PostScreenMessage(reason, 4f, ScreenMessageStyle.UPPER_CENTER);
                return;
            }
            if (center != null && center.IsKKSite)
            {
                KKBridge.SetEditorLaunchSite(center.KKSiteName, facility == EditorFacility.SPH);
            }
            if (!SaveGame()) return;
            EditorDriver.StartEditor(facility);
        }

        public static void OpenTrackingStation()
        {
            string reason;
            if (!CanLeaveFlight(out reason))
            {
                ScreenMessages.PostScreenMessage(reason, 4f, ScreenMessageStyle.UPPER_CENTER);
                return;
            }
            if (!SaveGame()) return;
            HighLogic.LoadScene(GameScenes.TRACKSTATION);
        }

        /// <summary>Go to the Space Center; a selected off-world base is honoured by the LoadScene redirect.</summary>
        public static void OpenSpaceCenter()
        {
            OpenSpaceCenter(false);
        }

        public static void OpenSpaceCenter(bool forceRealKSC)
        {
            string reason;
            if (!CanLeaveFlight(out reason))
            {
                ScreenMessages.PostScreenMessage(reason, 4f, ScreenMessageStyle.UPPER_CENTER);
                return;
            }
            if (HighLogic.LoadedScene == GameScenes.FLIGHT && !SaveGame()) return;
            if (forceRealKSC) GhostSpaceCenter.SuppressRedirectOnce = true;
            HighLogic.LoadScene(GameScenes.SPACECENTER);
        }

        /// <summary>Open the chosen facility of a center from wherever we are.</summary>
        public static void OpenFacility(KolonyCenter center, FacilityRole role)
        {
            switch (role)
            {
                case FacilityRole.VAB: OpenEditor(center, EditorFacility.VAB); return;
                case FacilityRole.SPH: OpenEditor(center, EditorFacility.SPH); return;
                case FacilityRole.TrackingStation:
                    if (HighLogic.LoadedScene == GameScenes.FLIGHT) OpenTrackingStation();
                    else HighLogic.LoadScene(GameScenes.TRACKSTATION);
                    return;
                case FacilityRole.AstronautComplex:
                case FacilityRole.ResearchAndDevelopment:
                case FacilityRole.MissionControl:
                case FacilityRole.Administration:
                    if (HighLogic.LoadedScene == GameScenes.SPACECENTER)
                    {
                        FireFacilityEvent(role);
                    }
                    else if (GhostView.Active)
                    {
                        GhostView.GoToRealSpaceCenter(role);
                    }
                    else
                    {
                        string reason;
                        if (!CanLeaveFlight(out reason))
                        {
                            ScreenMessages.PostScreenMessage(reason, 4f, ScreenMessageStyle.UPPER_CENTER);
                            return;
                        }
                        PendingFacility = role;
                        GhostSpaceCenter.SuppressRedirectOnce = true;
                        if (HighLogic.LoadedScene == GameScenes.FLIGHT && !SaveGame()) return;
                        HighLogic.LoadScene(GameScenes.SPACECENTER);
                    }
                    return;
            }
        }

        /// <summary>Space Center scene only: pops the stock facility UI.</summary>
        public static void FireFacilityEvent(FacilityRole role)
        {
            try
            {
                switch (role)
                {
                    case FacilityRole.AstronautComplex: GameEvents.onGUIAstronautComplexSpawn.Fire(); break;
                    case FacilityRole.ResearchAndDevelopment: GameEvents.onGUIRnDComplexSpawn.Fire(); break;
                    case FacilityRole.MissionControl: GameEvents.onGUIMissionControlSpawn.Fire(); break;
                    case FacilityRole.Administration: GameEvents.onGUIAdministrationFacilitySpawn.Fire(); break;
                }
            }
            catch (Exception e)
            {
                Log.Warn("Facility open failed: " + e.Message);
            }
        }

        /// <summary>Stock recovery flow: the same event the altimeter Recover button fires.</summary>
        public static void RecoverActiveVessel()
        {
            Vessel v = FlightGlobals.ActiveVessel;
            if (v == null) return;
            string reason;
            if (!CanLeaveFlight(out reason))
            {
                ScreenMessages.PostScreenMessage(reason, 4f, ScreenMessageStyle.UPPER_CENTER);
                return;
            }
            if (!v.IsRecoverable)
            {
                ScreenMessages.PostScreenMessage("Vessel is not inside a flagged base's recovery radius.", 4f, ScreenMessageStyle.UPPER_CENTER);
                return;
            }
            KolonyCenter center = null;
            if (KolonyScenario.Instance != null) KolonyScenario.Instance.IsRecoverableAtCenter(v, out center);
            bool stopped = center != null ? center.IsStopped(v) : v.horizontalSrfSpeed <= 0.3d;
            if (!stopped)
            {
                ScreenMessages.PostScreenMessage(center != null && center.IsVessel ? "Match velocity with the station before recovering." : "Come to a stop before recovering.", 3f, ScreenMessageStyle.UPPER_CENTER);
                return;
            }
            GameEvents.OnVesselRecoveryRequested.Fire(v);
        }

        /// <summary>Vessels (loaded or not) inside the center's radius: landed ones for ground centers, anything near a station.</summary>
        public static List<Vessel> VesselsAt(KolonyCenter center)
        {
            List<Vessel> list = new List<Vessel>();
            if (center == null || !HighLogic.LoadedSceneHasPlanetarium || FlightGlobals.Vessels == null) return list;
            foreach (Vessel v in FlightGlobals.Vessels)
            {
                if (v == null || v.mainBody == null) continue;
                if (GhostSpaceCenter.IsGhost(v)) continue;
                if (center.IsAnchor(v)) continue;
                if (!center.IsVessel && !v.LandedOrSplashed) continue;
                if (center.Contains(v)) list.Add(v);
            }
            return list;
        }

        public static int CrewAt(KolonyCenter center)
        {
            int n = 0;
            foreach (Vessel v in VesselsAt(center)) n += v.GetCrewCount();
            return n;
        }

        /// <summary>Pick the most "base-like" vessel to focus for a base view.</summary>
        public static Vessel PickViewVessel(KolonyCenter center)
        {
            Vessel best = null;
            int bestScore = int.MinValue;
            foreach (Vessel v in VesselsAt(center))
            {
                int score = 0;
                switch (v.vesselType)
                {
                    case VesselType.Base: score += 50; break;
                    case VesselType.Station: score += 40; break;
                    case VesselType.Lander: score += 30; break;
                    case VesselType.Rover: score += 25; break;
                    case VesselType.Ship: score += 20; break;
                    case VesselType.Plane: score += 20; break;
                    case VesselType.Probe: score += 10; break;
                    case VesselType.Relay: score += 10; break;
                    case VesselType.Flag: score += 5; break;
                    case VesselType.Debris: score -= 50; break;
                    case VesselType.EVA: score -= 20; break;
                    case VesselType.SpaceObject: score -= 100; break;
                }
                score += Math.Min(v.GetCrewCount(), 10);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = v;
                }
            }
            return best;
        }

        /// <summary>From the Tracking Station / Space Center: jump to flight at a vessel parked at the base.</summary>
        public static bool OpenBaseView(KolonyCenter center)
        {
            if (center == null) return false;
            Vessel v = PickViewVessel(center);
            if (v == null)
            {
                ScreenMessages.PostScreenMessage("No vessel is parked at " + center.Name + " to view from. Land something there (even a flag) first.", 6f, ScreenMessageStyle.UPPER_CENTER);
                return false;
            }
            if (HighLogic.LoadedScene == GameScenes.FLIGHT)
            {
                string reason;
                if (!CanLeaveFlight(out reason))
                {
                    ScreenMessages.PostScreenMessage(reason, 4f, ScreenMessageStyle.UPPER_CENTER);
                    return false;
                }
            }
            int idx = FlightGlobals.Vessels.IndexOf(v);
            if (idx < 0) return false;
            if (!SaveGame()) return false;
            PendingBaseViewName = center.Name;
            FlightDriver.StartAndFocusVessel("persistent", idx);
            return true;
        }
    }
}

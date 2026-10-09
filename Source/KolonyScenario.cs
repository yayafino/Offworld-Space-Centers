using System;
using System.Collections.Generic;
using UnityEngine;

namespace KolonySpaceCenters
{
    /// <summary>
    /// Per-save storage of flagged space centers. Lives in persistent.sfs as SCENARIO { name = KolonyScenario }.
    /// </summary>
    [KSPScenario(ScenarioCreationOptions.AddToAllGames, GameScenes.SPACECENTER, GameScenes.EDITOR, GameScenes.FLIGHT, GameScenes.TRACKSTATION)]
    public class KolonyScenario : ScenarioModule
    {
        public static KolonyScenario Instance { get; private set; }

        public readonly List<KolonyCenter> Centers = new List<KolonyCenter>();

        /// <summary>Name of the center the Space Center scene should open at. Empty = stock KSC.</summary>
        public string SelectedSpaceCenter = "";

        public static event Action OnCentersChanged;

        public override void OnAwake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public override void OnLoad(ConfigNode node)
        {
            Centers.Clear();
            foreach (ConfigNode n in node.GetNodes("CENTER"))
            {
                try
                {
                    Centers.Add(KolonyCenter.Load(n));
                }
                catch (Exception e)
                {
                    Log.Warn("Failed to load a center node: " + e.Message);
                }
            }
            SelectedSpaceCenter = node.GetValue("selectedSpaceCenter") ?? "";
            Log.Info("Loaded " + Centers.Count + " flagged space center(s).");
            NativeSpaceCenter.RememberSelection(SelectedCenter);
            FireChanged(false);
        }

        public override void OnSave(ConfigNode node)
        {
            foreach (KolonyCenter c in Centers)
            {
                c.Save(node.AddNode("CENTER"));
            }
            node.AddValue("selectedSpaceCenter", SelectedSpaceCenter ?? "");
        }

        // ---------------------------------------------------------------- queries

        public KolonyCenter GetByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            for (int i = 0; i < Centers.Count; i++)
            {
                if (Centers[i].Name == name) return Centers[i];
            }
            return null;
        }

        public KolonyCenter GetByKKSite(string siteName)
        {
            if (string.IsNullOrEmpty(siteName)) return null;
            for (int i = 0; i < Centers.Count; i++)
            {
                if (Centers[i].KKSiteName == siteName) return Centers[i];
            }
            return null;
        }

        public KolonyCenter SelectedCenter { get { return GetByName(SelectedSpaceCenter); } }

        /// <summary>True if this center may currently act as a recovery point.</summary>
        public bool IsActive(KolonyCenter c)
        {
            if (c == null || !c.Enabled) return false;
            if (c.IsVessel && !c.AnchorExists) return false;
            if (c.IsKKSite && KolonySettings.RequireOpenKKSite && HighLogic.CurrentGame != null
                && HighLogic.CurrentGame.Mode == Game.Modes.CAREER)
            {
                return KKBridge.IsSiteOpen(c.KKSiteName);
            }
            return true;
        }

        /// <summary>Nearest active ground center on the given body (any distance). Vessel centers are not considered here.</summary>
        public KolonyCenter Nearest(CelestialBody body, double lat, double lon, out double distance)
        {
            KolonyCenter best = null;
            distance = double.PositiveInfinity;
            if (body == null) return null;
            for (int i = 0; i < Centers.Count; i++)
            {
                KolonyCenter c = Centers[i];
                if (c.IsVessel || !IsActive(c)) continue;
                double d = c.SurfaceDistanceTo(body, lat, lon);
                if (d < distance)
                {
                    distance = d;
                    best = c;
                }
            }
            return best;
        }

        /// <summary>Nearest active center to a vessel (ground centers by surface distance, stations by 3D distance).</summary>
        public KolonyCenter Nearest(Vessel v, out double distance)
        {
            KolonyCenter best = null;
            distance = double.PositiveInfinity;
            if (v == null) return null;
            for (int i = 0; i < Centers.Count; i++)
            {
                KolonyCenter c = Centers[i];
                if (!IsActive(c)) continue;
                double d = c.DistanceTo(v);
                if (d < distance)
                {
                    distance = d;
                    best = c;
                }
            }
            return best;
        }

        /// <summary>Nearest active ground center whose radius contains the point, or null.</summary>
        public KolonyCenter FindContaining(CelestialBody body, double lat, double lon, out double distance)
        {
            KolonyCenter c = Nearest(body, lat, lon, out distance);
            if (c != null && distance <= c.RadiusMeters) return c;
            return null;
        }

        public KolonyCenter FindContaining(Vessel v, out double distance)
        {
            KolonyCenter c = Nearest(v, out distance);
            if (c != null && distance <= c.RadiusMeters) return c;
            return null;
        }

        public KolonyCenter FindContaining(ProtoVessel pv, out double distance)
        {
            KolonyCenter best = null;
            distance = double.PositiveInfinity;
            if (pv == null) return null;
            for (int i = 0; i < Centers.Count; i++)
            {
                KolonyCenter c = Centers[i];
                if (!IsActive(c)) continue;
                double d = c.DistanceTo(pv);
                if (d < distance)
                {
                    distance = d;
                    best = c;
                }
            }
            if (best != null && distance <= best.RadiusMeters) return best;
            return null;
        }

        /// <summary>
        /// Should the stock recovery system treat this vessel as recoverable because of a flagged base?
        /// Ground centers need the vessel landed/splashed; station centers accept anything inside the radius
        /// (the station itself is never recoverable through its own center).
        /// </summary>
        public bool IsRecoverableAtCenter(Vessel v)
        {
            KolonyCenter c;
            return IsRecoverableAtCenter(v, out c);
        }

        public bool IsRecoverableAtCenter(Vessel v, out KolonyCenter center)
        {
            center = null;
            if (!KolonySettings.RecoveryEnabled) return false;
            if (v == null || v.state == Vessel.State.DEAD) return false;
            if (GhostSpaceCenter.IsGhost(v)) return false;
            double d;
            KolonyCenter c = FindContaining(v, out d);
            if (c == null) return false;
            if (!c.IsVessel && !v.LandedOrSplashed) return false;
            center = c;
            return true;
        }

        public bool IsRecoverableAtCenter(ProtoVessel pv, out KolonyCenter center)
        {
            center = null;
            if (!KolonySettings.RecoveryEnabled) return false;
            if (pv == null || GhostSpaceCenter.IsGhost(pv)) return false;
            double d;
            KolonyCenter c = FindContaining(pv, out d);
            if (c == null) return false;
            if (!c.IsVessel && pv.situation != Vessel.Situations.LANDED && pv.situation != Vessel.Situations.SPLASHED && pv.situation != Vessel.Situations.PRELAUNCH) return false;
            center = c;
            return true;
        }

        // ---------------------------------------------------------------- edits

        public KolonyCenter FlagKKSite(KKBridge.SiteInfo site)
        {
            if (site == null) return null;
            KolonyCenter existing = GetByKKSite(site.Name);
            if (existing != null) return existing;
            KolonyCenter c = new KolonyCenter();
            c.Name = site.Name;
            c.BodyName = site.BodyName;
            c.Latitude = site.Latitude;
            c.Longitude = site.Longitude;
            c.Altitude = site.Altitude;
            c.RadiusMeters = KolonySettings.DefaultRadiusMeters;
            c.KKSiteName = site.Name;
            Centers.Add(c);
            Log.Info("Flagged KK site as space center: " + c);
            FireChanged();
            return c;
        }

        public KolonyCenter AddCustom(string name, CelestialBody body, double lat, double lon, double alt)
        {
            if (body == null) return null;
            string unique = string.IsNullOrEmpty(name) ? body.displayName + " Base" : name;
            int n = 2;
            while (GetByName(unique) != null) unique = name + " " + (n++);
            KolonyCenter c = new KolonyCenter();
            c.Name = unique;
            c.BodyName = body.name;
            c.Latitude = lat;
            c.Longitude = lon;
            c.Altitude = alt;
            c.RadiusMeters = KolonySettings.DefaultRadiusMeters;
            Centers.Add(c);
            Log.Info("Added custom space center: " + c);
            FireChanged();
            return c;
        }

        public KolonyCenter GetByVessel(Guid id)
        {
            for (int i = 0; i < Centers.Count; i++)
            {
                if (Centers[i].IsVessel && Centers[i].VesselGuid == id) return Centers[i];
            }
            return null;
        }

        /// <summary>Flag a vessel (station, ship, mobile base) as a space center that moves with it.</summary>
        public KolonyCenter FlagVessel(Vessel v, string name)
        {
            if (v == null) return null;
            KolonyCenter existing = GetByVessel(v.id);
            if (existing != null) return existing;
            KolonyCenter c = new KolonyCenter();
            c.Name = string.IsNullOrEmpty(name) ? v.vesselName : name;
            int n = 2;
            string baseName = c.Name;
            while (GetByName(c.Name) != null) c.Name = baseName + " " + (n++);
            c.BodyName = v.mainBody != null ? v.mainBody.name : "";
            c.Latitude = v.latitude;
            c.Longitude = v.longitude;
            c.Altitude = v.altitude;
            c.RadiusMeters = KolonySettings.DefaultRadiusMeters;
            c.VesselId = v.id.ToString();
            Centers.Add(c);
            Log.Info("Flagged vessel as space center: " + c);
            FireChanged();
            return c;
        }

        public void Remove(KolonyCenter c)
        {
            if (c == null) return;
            if (Centers.Remove(c))
            {
                if (SelectedSpaceCenter == c.Name) SelectedSpaceCenter = "";
                Log.Info("Removed space center: " + c);
                FireChanged();
            }
        }

        public void FireChanged()
        {
            FireChanged(true);
        }

        public void FireChanged(bool persist)
        {
            NativeSpaceCenter.RememberSelection(SelectedCenter);
            if (OnCentersChanged != null)
            {
                try { OnCentersChanged(); } catch (Exception e) { Log.Warn("OnCentersChanged handler threw: " + e); }
            }
            if (persist) PersistNow();
        }

        public void SelectSpaceCenter(string name)
        {
            SelectedSpaceCenter = name ?? "";
            NativeSpaceCenter.RememberSelection(SelectedCenter);
            PersistNow();
        }

        /// <summary>
        /// Write persistent.sfs right away so a scene reload (which reloads from disk) sees the change.
        /// In flight this only happens when the game is in a saveable state.
        /// </summary>
        public void PersistNow()
        {
            if (!KolonySettings.SaveOnChange) return;
            if (HighLogic.CurrentGame == null || !HighLogic.LoadedSceneIsGame) return;
            try
            {
                if (HighLogic.LoadedScene == GameScenes.FLIGHT)
                {
                    if (FlightGlobals.ClearToSave() != ClearToSaveStatus.CLEAR) return;
                }
                GamePersistence.SaveGame("persistent", HighLogic.SaveFolder, SaveMode.OVERWRITE);
            }
            catch (Exception e)
            {
                Log.Warn("PersistNow failed: " + e.Message);
            }
        }
    }
}

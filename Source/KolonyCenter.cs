using System;
using System.Collections.Generic;
using UnityEngine;

namespace KolonySpaceCenters
{
    /// <summary>Roles a Kerbal Konstructs building can be given at a center.</summary>
    public enum FacilityRole
    {
        None = 0,
        VAB,
        SPH,
        TrackingStation,
        AstronautComplex,
        ResearchAndDevelopment,
        MissionControl,
        Administration
    }

    public static class FacilityRoles
    {
        public static readonly FacilityRole[] Assignable =
        {
            FacilityRole.VAB, FacilityRole.SPH, FacilityRole.TrackingStation, FacilityRole.AstronautComplex,
            FacilityRole.ResearchAndDevelopment, FacilityRole.MissionControl, FacilityRole.Administration
        };

        public static string DisplayName(FacilityRole r)
        {
            switch (r)
            {
                case FacilityRole.VAB: return "Vehicle Assembly Building";
                case FacilityRole.SPH: return "Spaceplane Hangar";
                case FacilityRole.TrackingStation: return "Tracking Station";
                case FacilityRole.AstronautComplex: return "Astronaut Complex";
                case FacilityRole.ResearchAndDevelopment: return "Research and Development";
                case FacilityRole.MissionControl: return "Mission Control";
                case FacilityRole.Administration: return "Administration Building";
                default: return "None";
            }
        }

        public static string ShortName(FacilityRole r)
        {
            switch (r)
            {
                case FacilityRole.VAB: return "VAB";
                case FacilityRole.SPH: return "SPH";
                case FacilityRole.TrackingStation: return "Tracking";
                case FacilityRole.AstronautComplex: return "Astronauts";
                case FacilityRole.ResearchAndDevelopment: return "R&D";
                case FacilityRole.MissionControl: return "Mission Ctrl";
                case FacilityRole.Administration: return "Admin";
                default: return "None";
            }
        }
    }

    /// <summary>
    /// One flagged "space center": a point on a celestial body with a recovery radius.
    /// Usually anchored to a Kerbal Konstructs launch site, but can also be a free marker
    /// dropped at a vessel's location (for bases built from parts rather than statics).
    /// </summary>
    public class KolonyCenter
    {
        public string Name = "Unnamed Base";
        public string BodyName = "";
        public double Latitude;
        public double Longitude;
        public double Altitude;
        public double RadiusMeters = 10000d;
        /// <summary>Kerbal Konstructs launch site name this center is anchored to, or empty for a free marker.</summary>
        public string KKSiteName = "";
        /// <summary>Vessel id (Guid) this center is anchored to, for stations and other vessel-based bases; empty otherwise.</summary>
        public string VesselId = "";
        public bool Enabled = true;

        public bool IsVessel { get { return !string.IsNullOrEmpty(VesselId); } }

        private Guid? _guid;
        public Guid VesselGuid
        {
            get
            {
                if (_guid == null)
                {
                    Guid g;
                    _guid = Guid.TryParse(VesselId, out g) ? g : Guid.Empty;
                }
                return _guid.Value;
            }
        }

        /// <summary>The anchor vessel (loaded or not), or null if it no longer exists / not in a planetarium scene.</summary>
        public Vessel AnchorVessel
        {
            get
            {
                if (!IsVessel || !HighLogic.LoadedSceneHasPlanetarium) return null;
                try { return FlightGlobals.FindVessel(VesselGuid); } catch { return null; }
            }
        }

        /// <summary>Proto vessel of the anchor, for scenes without live vessels (editor).</summary>
        public ProtoVessel AnchorProto
        {
            get
            {
                if (!IsVessel || HighLogic.CurrentGame == null || HighLogic.CurrentGame.flightState == null) return null;
                Guid g = VesselGuid;
                foreach (ProtoVessel pv in HighLogic.CurrentGame.flightState.protoVessels)
                {
                    if (pv != null && pv.vesselID == g) return pv;
                }
                return null;
            }
        }

        public bool AnchorExists
        {
            get
            {
                if (!IsVessel) return true;
                return AnchorVessel != null || AnchorProto != null;
            }
        }

        public bool IsAnchor(Vessel v) { return IsVessel && v != null && v.id == VesselGuid; }
        public bool IsAnchor(ProtoVessel pv) { return IsVessel && pv != null && pv.vesselID == VesselGuid; }

        /// <summary>Current world position of the anchor vessel, or null.</summary>
        public Vector3d? AnchorPosition
        {
            get
            {
                Vessel v = AnchorVessel;
                if (v != null)
                {
                    try { return v.GetWorldPos3D(); } catch { }
                }
                ProtoVessel pv = AnchorProto;
                return PositionOf(pv);
            }
        }

        public static Vector3d? PositionOf(ProtoVessel pv)
        {
            if (pv == null) return null;
            try
            {
                if (pv.vesselRef != null) return pv.vesselRef.GetWorldPos3D();
                if (pv.orbitSnapShot != null)
                {
                    Orbit o = pv.orbitSnapShot.Load();
                    if (o != null && o.referenceBody != null) return o.getPositionAtUT(Planetarium.GetUniversalTime());
                }
            }
            catch { }
            return null;
        }

        public static CelestialBody BodyOf(ProtoVessel pv)
        {
            if (pv == null) return null;
            if (pv.vesselRef != null && pv.vesselRef.mainBody != null) return pv.vesselRef.mainBody;
            if (pv.orbitSnapShot != null)
            {
                int idx = pv.orbitSnapShot.ReferenceBodyIndex;
                if (idx >= 0 && idx < FlightGlobals.Bodies.Count) return FlightGlobals.Bodies[idx];
            }
            return null;
        }

        /// <summary>Refresh the cached body name from the anchor vessel (stations can change SOI).</summary>
        public void SyncFromAnchor()
        {
            if (!IsVessel) return;
            Vessel v = AnchorVessel;
            if (v != null && v.mainBody != null) { BodyName = v.mainBody.name; return; }
            CelestialBody b = BodyOf(AnchorProto);
            if (b != null) BodyName = b.name;
        }

        /// <summary>Distance from this center to a vessel, in meters. +Inf when not applicable (other body, the anchor itself, missing anchor).</summary>
        public double DistanceTo(Vessel v)
        {
            if (v == null) return double.PositiveInfinity;
            if (!IsVessel) return SurfaceDistanceTo(v.mainBody, v.latitude, v.longitude);
            if (IsAnchor(v)) return double.PositiveInfinity;
            Vessel anchor = AnchorVessel;
            if (anchor == null) return double.PositiveInfinity;
            if (anchor.mainBody != v.mainBody) return double.PositiveInfinity;
            try { return (anchor.GetWorldPos3D() - v.GetWorldPos3D()).magnitude; }
            catch { return double.PositiveInfinity; }
        }

        public double DistanceTo(ProtoVessel pv)
        {
            if (pv == null) return double.PositiveInfinity;
            if (!IsVessel) return SurfaceDistanceTo(BodyOf(pv), pv.latitude, pv.longitude);
            if (IsAnchor(pv)) return double.PositiveInfinity;
            Vector3d? a = AnchorPosition;
            Vector3d? b = PositionOf(pv);
            if (a == null || b == null) return double.PositiveInfinity;
            CelestialBody ab = Body;
            CelestialBody bb = BodyOf(pv);
            if (ab != null && bb != null && ab != bb) return double.PositiveInfinity;
            return (a.Value - b.Value).magnitude;
        }

        public bool Contains(Vessel v) { return DistanceTo(v) <= RadiusMeters; }
        public bool Contains(ProtoVessel pv) { return DistanceTo(pv) <= RadiusMeters; }

        /// <summary>Relative speed to the anchor (vessel centers) or surface speed (ground centers).</summary>
        public double ApproachSpeed(Vessel v)
        {
            if (v == null) return double.PositiveInfinity;
            if (!IsVessel) return v.horizontalSrfSpeed;
            Vessel anchor = AnchorVessel;
            if (anchor == null) return double.PositiveInfinity;
            try { return (anchor.obt_velocity - v.obt_velocity).magnitude; } catch { return double.PositiveInfinity; }
        }

        /// <summary>Stopped enough for the Recover button: &lt; 0.3 m/s on the ground, &lt; 1 m/s relative to a station.</summary>
        public bool IsStopped(Vessel v)
        {
            return ApproachSpeed(v) < (IsVessel ? 1.0d : 0.3d);
        }

        /// <summary>Kerbal Konstructs static UUID → facility role, for buildings designated at this center.</summary>
        public readonly Dictionary<string, FacilityRole> Facilities = new Dictionary<string, FacilityRole>();

        public FacilityRole RoleOf(string uuid)
        {
            FacilityRole r;
            if (!string.IsNullOrEmpty(uuid) && Facilities.TryGetValue(uuid, out r)) return r;
            return FacilityRole.None;
        }

        public string UuidFor(FacilityRole role)
        {
            foreach (KeyValuePair<string, FacilityRole> kv in Facilities) if (kv.Value == role) return kv.Key;
            return null;
        }

        /// <summary>Assign a role to a building; a role can only be held by one building, and a building holds one role.</summary>
        public void Assign(string uuid, FacilityRole role)
        {
            if (string.IsNullOrEmpty(uuid)) return;
            if (role == FacilityRole.None)
            {
                Facilities.Remove(uuid);
                return;
            }
            string previous = UuidFor(role);
            if (previous != null && previous != uuid) Facilities.Remove(previous);
            Facilities[uuid] = role;
        }

        public bool IsKKSite { get { return !string.IsNullOrEmpty(KKSiteName); } }

        private CelestialBody _body;
        public CelestialBody Body
        {
            get
            {
                if (_body == null || _body.name != BodyName)
                {
                    _body = FlightGlobals.GetBodyByName(BodyName);
                }
                return _body;
            }
        }

        /// <summary>True when the stock Space Center scene (home world camera relocation) can show this center.</summary>
        public bool IsOnHomeWorld
        {
            get
            {
                if (IsVessel) return false;
                CelestialBody b = Body;
                return b != null && b.isHomeWorld;
            }
        }

        /// <summary>Great-circle surface distance in meters, or +Inf if the body does not match.</summary>
        public double SurfaceDistanceTo(CelestialBody body, double lat, double lon)
        {
            if (body == null || body.name != BodyName) return double.PositiveInfinity;
            return GreatCircle(body.Radius, Latitude, Longitude, lat, lon);
        }

        public bool Contains(CelestialBody body, double lat, double lon)
        {
            return SurfaceDistanceTo(body, lat, lon) <= RadiusMeters;
        }

        public Vector3d WorldPosition
        {
            get
            {
                if (IsVessel)
                {
                    Vector3d? p = AnchorPosition;
                    return p != null ? p.Value : Vector3d.zero;
                }
                CelestialBody b = Body;
                if (b == null) return Vector3d.zero;
                return b.GetWorldSurfacePosition(Latitude, Longitude, Altitude);
            }
        }

        public static double GreatCircle(double radius, double lat1, double lon1, double lat2, double lon2)
        {
            double p1 = lat1 * Math.PI / 180d;
            double p2 = lat2 * Math.PI / 180d;
            double dp = p2 - p1;
            double dl = (lon2 - lon1) * Math.PI / 180d;
            double a = Math.Sin(dp / 2d) * Math.Sin(dp / 2d) + Math.Cos(p1) * Math.Cos(p2) * Math.Sin(dl / 2d) * Math.Sin(dl / 2d);
            a = Math.Min(1d, Math.Max(0d, a));
            double c = 2d * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1d - a));
            return radius * c;
        }

        public void Save(ConfigNode node)
        {
            node.AddValue("name", Name);
            node.AddValue("body", BodyName);
            node.AddValue("lat", Latitude.ToString("R"));
            node.AddValue("lon", Longitude.ToString("R"));
            node.AddValue("alt", Altitude.ToString("R"));
            node.AddValue("radius", RadiusMeters.ToString("R"));
            node.AddValue("kkSite", KKSiteName ?? "");
            node.AddValue("vesselId", VesselId ?? "");
            node.AddValue("enabled", Enabled);
            foreach (KeyValuePair<string, FacilityRole> kv in Facilities)
            {
                ConfigNode f = node.AddNode("FACILITY");
                f.AddValue("uuid", kv.Key);
                f.AddValue("role", kv.Value.ToString());
            }
        }

        public static KolonyCenter Load(ConfigNode node)
        {
            KolonyCenter c = new KolonyCenter();
            c.Name = node.GetValue("name") ?? c.Name;
            c.BodyName = node.GetValue("body") ?? "";
            double d;
            if (double.TryParse(node.GetValue("lat"), out d)) c.Latitude = d;
            if (double.TryParse(node.GetValue("lon"), out d)) c.Longitude = d;
            if (double.TryParse(node.GetValue("alt"), out d)) c.Altitude = d;
            if (double.TryParse(node.GetValue("radius"), out d)) c.RadiusMeters = d;
            c.KKSiteName = node.GetValue("kkSite") ?? "";
            c.VesselId = node.GetValue("vesselId") ?? "";
            bool b;
            if (bool.TryParse(node.GetValue("enabled"), out b)) c.Enabled = b;
            foreach (ConfigNode f in node.GetNodes("FACILITY"))
            {
                string uuid = f.GetValue("uuid");
                string roleName = f.GetValue("role");
                if (string.IsNullOrEmpty(uuid) || string.IsNullOrEmpty(roleName)) continue;
                try
                {
                    FacilityRole role = (FacilityRole)Enum.Parse(typeof(FacilityRole), roleName);
                    if (role != FacilityRole.None) c.Facilities[uuid] = role;
                }
                catch { }
            }
            return c;
        }

        public string KindLabel
        {
            get
            {
                if (IsVessel) return "vessel";
                if (IsKKSite) return "KK launch site";
                return "marker";
            }
        }

        public override string ToString()
        {
            return Name + " (" + BodyName + ")";
        }
    }
}

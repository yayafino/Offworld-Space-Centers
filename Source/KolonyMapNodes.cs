using System;
using System.Collections;
using System.Collections.Generic;
using KSP.UI.Screens.Mapview;
using UnityEngine;

namespace KolonySpaceCenters
{
    /// <summary>
    /// Shows every flagged space center in the Tracking Station and map view with the same icon the stock
    /// launch sites (Dessert Airfield, Woomerang) use. This mirrors what stock's private SiteNode does, through
    /// the public MapNode API, and adds a navigation waypoint so the base can be targeted from flight.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.AllGameScenes, false)]
    public class KolonyMapNodes : MonoBehaviour
    {
        private readonly List<CenterNode> nodes = new List<CenterNode>();
        private bool dirty;

        private void Awake()
        {
            GameScenes s = HighLogic.LoadedScene;
            if (s != GameScenes.TRACKSTATION && s != GameScenes.FLIGHT)
            {
                enabled = false;
                Destroy(this);
            }
        }

        private void Start()
        {
            KolonyScenario.OnCentersChanged += MarkDirty;
            StartCoroutine(Build());
        }

        private void OnDestroy()
        {
            KolonyScenario.OnCentersChanged -= MarkDirty;
            Clear();
        }

        private void MarkDirty() { dirty = true; }

        private IEnumerator Build()
        {
            // MapView and the scenario need to exist first
            float t = 0f;
            while ((MapView.fetch == null || KolonyScenario.Instance == null) && t < 15f)
            {
                t += Time.deltaTime;
                yield return null;
            }
            yield return null;
            Rebuild();
        }

        private void Update()
        {
            if (dirty && MapView.fetch != null && KolonyScenario.Instance != null)
            {
                dirty = false;
                Rebuild();
            }
        }

        private void LateUpdate()
        {
            for (int i = 0; i < nodes.Count; i++) nodes[i].Tick();
        }

        private void Clear()
        {
            foreach (CenterNode n in nodes) n.Destroy();
            nodes.Clear();
        }

        private void Rebuild()
        {
            Clear();
            KolonyScenario sc = KolonyScenario.Instance;
            if (sc == null || MapView.fetch == null) return;
            foreach (KolonyCenter c in sc.Centers)
            {
                if (c.IsVessel) continue; // the station already has its own map icon
                try
                {
                    CenterNode n = new CenterNode(c);
                    if (n.Create()) nodes.Add(n);
                }
                catch (Exception e)
                {
                    Log.Warn("Map node for " + c + " failed: " + e.Message);
                }
            }
        }

        /// <summary>One map icon + waypoint for one center.</summary>
        private class CenterNode
        {
            private readonly KolonyCenter center;
            private MapNode node;
            private FinePrint.Waypoint waypoint;

            public CenterNode(KolonyCenter c) { center = c; }

            public bool Create()
            {
                CelestialBody body = center.Body;
                if (body == null) return false;

                node = MapNode.Create(center.Name, Color.white, 24, true, false, false);
                if (node == null) return false;
                node.OnUpdateVisible += OnUpdateVisible;
                node.OnUpdatePosition += OnUpdatePosition;
                node.OnUpdateCaption += OnUpdateCaption;
                node.OnUpdateType += OnUpdateType;
                node.OnClick += OnClick;

                // navigation waypoint, same flags stock uses for its launch sites
                waypoint = new FinePrint.Waypoint();
                waypoint.celestialName = body.name;
                waypoint.latitude = center.Latitude;
                waypoint.longitude = center.Longitude;
                waypoint.altitude = 0d;
                waypoint.name = center.Name;
                waypoint.id = IsRunway() ? "runway" : "launchsite";
                waypoint.index = 0;
                waypoint.seed = -1;
                waypoint.landLocked = true;
                waypoint.isOnSurface = true;
                waypoint.isNavigatable = true;
                waypoint.node = node;
                waypoint.SetFadeRange(-1d);
                FinePrint.WaypointManager.AddWaypoint(waypoint);
                return true;
            }

            private bool IsRunway()
            {
                // SPH-type KK launch sites get the runway glyph
                if (!center.IsKKSite) return false;
                foreach (KKBridge.SiteInfo s in KKBridge.GetLaunchSites())
                {
                    if (s.Name == center.KKSiteName) return s.Category != null && s.Category.IndexOf("Runway", StringComparison.OrdinalIgnoreCase) >= 0;
                }
                return false;
            }

            public void Tick()
            {
                if (node != null) node.NodeUpdate();
            }

            public void Destroy()
            {
                try
                {
                    if (waypoint != null) FinePrint.WaypointManager.RemoveWaypoint(waypoint);
                    waypoint = null;
                    if (node != null) node.Terminate();
                    node = null;
                }
                catch (Exception e) { Log.Warn("Map node cleanup: " + e.Message); }
            }

            private void OnUpdateVisible(MapNode n, MapNode.IconData data)
            {
                bool filterOn = (MapViewFiltering.vesselTypeFilter & (MapViewFiltering.VesselTypeFilter)8192) != 0; // launch sites filter
                data.visible = MapView.MapIsEnabled && filterOn && center.Enabled;
            }

            private Vector3d OnUpdatePosition(MapNode n)
            {
                return ScaledSpace.LocalToScaledSpace(center.WorldPosition);
            }

            private void OnUpdateCaption(MapNode n, MapNode.CaptionData data)
            {
                data.Header = center.Name;
                data.captionLine1 = "Space Center on " + center.BodyName.Replace("^N", "");
                int vessels = SceneActions.VesselsAt(center).Count;
                data.captionLine2 = vessels + " vessel(s) within " + FlightMonitor.FormatDistance(center.RadiusMeters);
                data.captionLine3 = center.IsKKSite ? "Launch site: " + center.KKSiteName : "Marker";
            }

            private void OnUpdateType(MapNode n, MapNode.TypeData data)
            {
                data.oType = MapObject.ObjectType.Site;
                data.sType = IsRunway() ? MapNode.SiteType.Runway : MapNode.SiteType.LaunchSite;
            }

            private void OnClick(MapNode n, Mouse.Buttons b)
            {
                if (b != Mouse.Buttons.Left) return;
                if (KolonyWindow.Instance != null) KolonyWindow.Instance.ShowCenter(center);
            }
        }
    }
}

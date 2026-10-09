using System;
using System.Collections.Generic;
using UnityEngine;

namespace KolonySpaceCenters
{
    /// <summary>
    /// Makes designated Kerbal Konstructs buildings behave like KSC facilities: hover highlights them and shows
    /// their role, clicking opens the facility. Also implements "click a building to assign it a role".
    /// Works in the Space Center scene (home-world camera relocation) and in flight (off-world Space Center view).
    /// </summary>
    [KSPAddon(KSPAddon.Startup.AllGameScenes, false)]
    public class FacilityBinder : MonoBehaviour
    {
        public static FacilityBinder Instance { get; private set; }

        /// <summary>When not None, the next clicked building gets this role at AssignCenter.</summary>
        public FacilityRole PendingAssignRole = FacilityRole.None;
        public KolonyCenter AssignCenter;

        private string hoverUuid;
        private string hoverText;
        private GUIStyle tipStyle;
        private Part hoverPart;

        private static readonly Color HoverColor = new Color(0.35f, 0.75f, 1f, 1f);
        private static readonly Color AssignColor = new Color(1f, 0.75f, 0.2f, 1f);

        private void Awake()
        {
            GameScenes s = HighLogic.LoadedScene;
            if (s != GameScenes.SPACECENTER && s != GameScenes.FLIGHT)
            {
                enabled = false;
                Destroy(this);
                return;
            }
            Instance = this;
        }

        private void Start()
        {
            if (!KKBridge.Available) return;
            KKBridge.EnsureClickHooks();
            KKBridge.OnStaticClicked += OnClicked;
            KKBridge.OnStaticHoverEnter += OnEnter;
            KKBridge.OnStaticHoverExit += OnExit;
        }

        private void OnDestroy()
        {
            KKBridge.OnStaticClicked -= OnClicked;
            KKBridge.OnStaticHoverEnter -= OnEnter;
            KKBridge.OnStaticHoverExit -= OnExit;
            if (hoverUuid != null) KKBridge.Unhighlight(hoverUuid);
            if (Instance == this) Instance = null;
        }

        /// <summary>The center whose buildings are "live" right now.</summary>
        public static KolonyCenter ViewedCenter
        {
            get
            {
                if (HighLogic.LoadedScene == GameScenes.SPACECENTER)
                {
                    if (SpaceCenterRelocator.Relocated) return SpaceCenterRelocator.RelocatedTo;
                    return null;
                }
                if (GhostView.Active) return GhostView.Center;
                FlightMonitor fm = FlightMonitor.Instance;
                if (fm != null && fm.CurrentCenter != null) return fm.CurrentCenter;
                return null;
            }
        }

        private static KolonyCenter CenterFor(string uuid, out FacilityRole role)
        {
            role = FacilityRole.None;
            KolonyScenario sc = KolonyScenario.Instance;
            if (sc == null) return null;
            KolonyCenter viewed = ViewedCenter;
            if (viewed != null)
            {
                role = viewed.RoleOf(uuid);
                if (role != FacilityRole.None) return viewed;
            }
            foreach (KolonyCenter c in sc.Centers)
            {
                role = c.RoleOf(uuid);
                if (role != FacilityRole.None) return c;
            }
            return null;
        }

        public void BeginAssign(KolonyCenter center, FacilityRole role)
        {
            AssignCenter = center;
            PendingAssignRole = role;
            ScreenMessages.PostScreenMessage("Click a " + (center.IsVessel ? "part of the station" : "building") + " to make it the " + FacilityRoles.DisplayName(role) + " of " + center.Name + " (Esc or button again to cancel)", 6f, ScreenMessageStyle.UPPER_CENTER);
        }

        public void CancelAssign()
        {
            PendingAssignRole = FacilityRole.None;
            AssignCenter = null;
        }

        private void Update()
        {
            if (PendingAssignRole != FacilityRole.None && Input.GetKeyDown(KeyCode.Escape)) CancelAssign();
            if (HighLogic.LoadedScene == GameScenes.FLIGHT) PollParts();
        }

        // ------------------------------------------------------------------ station parts as facilities

        public const string PartPrefix = "part:";
        public static string PartKey(uint flightID) { return PartPrefix + flightID; }
        public static bool IsPartKey(string key) { return key != null && key.StartsWith(PartPrefix); }

        /// <summary>Human-readable name for a facility key (KK building title or part title).</summary>
        public static string FacilityTitle(KolonyCenter c, string key)
        {
            if (string.IsNullOrEmpty(key)) return "(none)";
            if (IsPartKey(key))
            {
                uint id;
                if (!uint.TryParse(key.Substring(PartPrefix.Length), out id)) return key;
                Vessel v = c != null ? c.AnchorVessel : null;
                if (v != null && v.loaded && v.parts != null)
                {
                    foreach (Part p in v.parts) if (p.flightID == id) return p.partInfo != null ? p.partInfo.title : p.name;
                }
                ProtoVessel pv = v != null ? v.protoVessel : (c != null ? c.AnchorProto : null);
                if (pv != null && pv.protoPartSnapshots != null)
                {
                    foreach (ProtoPartSnapshot pp in pv.protoPartSnapshots) if (pp.flightID == id) return pp.partInfo != null ? pp.partInfo.title : pp.partName;
                }
                return "(missing part " + id + ")";
            }
            KKBridge.StaticInfo si = KKBridge.GetStatic(key);
            return si != null ? si.Title : "(missing building " + key.Substring(0, Math.Min(8, key.Length)) + ")";
        }

        /// <summary>Parts of a station center that can be designated, as (key, title) pairs.</summary>
        public static List<KeyValuePair<string, string>> PartCandidates(KolonyCenter c)
        {
            List<KeyValuePair<string, string>> list = new List<KeyValuePair<string, string>>();
            if (c == null || !c.IsVessel) return list;
            Vessel v = c.AnchorVessel;
            if (v != null && v.loaded && v.parts != null)
            {
                foreach (Part p in v.parts) list.Add(new KeyValuePair<string, string>(PartKey(p.flightID), p.partInfo != null ? p.partInfo.title : p.name));
                return list;
            }
            ProtoVessel pv = v != null ? v.protoVessel : c.AnchorProto;
            if (pv != null && pv.protoPartSnapshots != null)
            {
                foreach (ProtoPartSnapshot pp in pv.protoPartSnapshots) list.Add(new KeyValuePair<string, string>(PartKey(pp.flightID), pp.partInfo != null ? pp.partInfo.title : pp.partName));
            }
            return list;
        }

        /// <summary>Which vessel center's parts should react to the mouse right now.</summary>
        private KolonyCenter PartCenter()
        {
            if (PendingAssignRole != FacilityRole.None && AssignCenter != null && AssignCenter.IsVessel) return AssignCenter;
            KolonyCenter viewed = ViewedCenter;
            if (viewed != null && viewed.IsVessel) return viewed;
            return null;
        }

        private void PollParts()
        {
            KolonyCenter c = PartCenter();
            Part hp = c != null ? Mouse.HoveredPart : null;
            if (hp != null && (hp.vessel == null || !c.IsAnchor(hp.vessel))) hp = null;

            if (hp != hoverPart)
            {
                if (hoverPart != null)
                {
                    try { hoverPart.SetHighlightDefault(); } catch { }
                    if (hoverText != null && hoverUuid == null) hoverText = null;
                }
                hoverPart = hp;
                if (hp != null)
                {
                    string key = PartKey(hp.flightID);
                    FacilityRole role = c.RoleOf(key);
                    if (PendingAssignRole != FacilityRole.None)
                    {
                        hoverText = "Assign " + FacilityRoles.DisplayName(PendingAssignRole) + ": " + (hp.partInfo != null ? hp.partInfo.title : hp.name);
                        hp.SetHighlightColor(AssignColor);
                        hp.SetHighlight(true, false);
                    }
                    else if (role != FacilityRole.None)
                    {
                        hoverText = FacilityRoles.DisplayName(role);
                        hp.SetHighlightColor(HoverColor);
                        hp.SetHighlight(true, false);
                    }
                }
            }

            if (hoverPart != null && Input.GetMouseButtonDown(0) && !KolonyWindow.MouseOverWindow)
            {
                string key = PartKey(hoverPart.flightID);
                if (PendingAssignRole != FacilityRole.None && AssignCenter == c)
                {
                    c.Assign(key, PendingAssignRole);
                    ScreenMessages.PostScreenMessage((hoverPart.partInfo != null ? hoverPart.partInfo.title : hoverPart.name) + " is now the " + FacilityRoles.DisplayName(PendingAssignRole) + " of " + c.Name, 5f, ScreenMessageStyle.UPPER_CENTER);
                    try { hoverPart.SetHighlightDefault(); } catch { }
                    hoverText = null;
                    CancelAssign();
                    if (KolonyScenario.Instance != null) KolonyScenario.Instance.FireChanged();
                    return;
                }
                FacilityRole role = c.RoleOf(key);
                if (role != FacilityRole.None)
                {
                    try { hoverPart.SetHighlightDefault(); } catch { }
                    hoverText = null;
                    SceneActions.OpenFacility(c, role);
                }
            }
        }

        private void OnEnter(string uuid)
        {
            FacilityRole role;
            KolonyCenter c = CenterFor(uuid, out role);
            if (PendingAssignRole != FacilityRole.None && AssignCenter != null)
            {
                hoverUuid = uuid;
                KKBridge.StaticInfo si = KKBridge.GetStatic(uuid);
                hoverText = "Assign " + FacilityRoles.DisplayName(PendingAssignRole) + ": " + (si != null ? si.Title : uuid);
                KKBridge.Highlight(uuid, AssignColor);
                return;
            }
            if (c == null) return;
            hoverUuid = uuid;
            hoverText = FacilityRoles.DisplayName(role);
            KKBridge.Highlight(uuid, HoverColor);
        }

        private void OnExit(string uuid)
        {
            if (hoverUuid == uuid)
            {
                KKBridge.Unhighlight(uuid);
                hoverUuid = null;
                hoverText = null;
            }
        }

        private void OnClicked(string uuid)
        {
            if (string.IsNullOrEmpty(uuid)) return;
            if (PendingAssignRole != FacilityRole.None && AssignCenter != null)
            {
                AssignCenter.Assign(uuid, PendingAssignRole);
                KKBridge.StaticInfo si = KKBridge.GetStatic(uuid);
                ScreenMessages.PostScreenMessage((si != null ? si.Title : "Building") + " is now the " + FacilityRoles.DisplayName(PendingAssignRole) + " of " + AssignCenter.Name, 5f, ScreenMessageStyle.UPPER_CENTER);
                KKBridge.Unhighlight(uuid);
                CancelAssign();
                if (KolonyScenario.Instance != null) KolonyScenario.Instance.FireChanged();
                return;
            }
            FacilityRole role;
            KolonyCenter c = CenterFor(uuid, out role);
            if (c == null || role == FacilityRole.None) return;
            // only react to buildings of the center we are looking at, or any center when nothing is "viewed"
            KolonyCenter viewed = ViewedCenter;
            if (viewed != null && viewed != c) return;
            KKBridge.Unhighlight(uuid);
            SceneActions.OpenFacility(c, role);
        }

        private void OnGUI()
        {
            if (hoverText == null) return;
            if (tipStyle == null)
            {
                tipStyle = new GUIStyle(HighLogic.Skin.box) { fontSize = 13, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            }
            Vector2 m = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            Vector2 size = tipStyle.CalcSize(new GUIContent(hoverText));
            GUI.Box(new Rect(m.x + 14f, m.y + 14f, size.x + 16f, size.y + 8f), hoverText, tipStyle);
        }
    }
}

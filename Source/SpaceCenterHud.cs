using System;
using System.Collections.Generic;
using UnityEngine;

namespace OffworldSpaceCenters
{
    /// <summary>
    /// Hides the flight-specific UI (navball, staging, altimeter, time/warp frame, portraits, docking and
    /// maneuver editors, mode buttons, resource panel) while a Space Center view is active, and keeps the rest
    /// (stock app toolbar, screen messages, dialogs) like the real KSC scene. Stock re-enables some of these on
    /// mode switches, so the hide is re-asserted every frame until Restore().
    /// </summary>
    public class FlightUiHider
    {
        private readonly Dictionary<GameObject, bool> previous = new Dictionary<GameObject, bool>();
        private bool collected;

        private void Collect()
        {
            previous.Clear();
            FlightUIModeController c = FlightUIModeController.Instance;
            if (c != null)
            {
                Add(c.altimeterFrame); Add(c.timeFrame); Add(c.stagingQuadrant); Add(c.dockingRotQuadrant); Add(c.dockingLinQuadrant);
                Add(c.manNodeHandleEditor); Add(c.manNodeEditor); Add(c.crew); Add(c.navBall); Add(c.uiModeFrame); Add(c.MapOptionsQuadrant);
                Add(c.UIScaleStageManager); Add(c.UIFrameCrewPortaits);
            }
            try
            {
                KSP.UI.Screens.ResourceDisplay rd = KSP.UI.Screens.ResourceDisplay.Instance;
                if (rd != null) Add(rd.gameObject);
            }
            catch { }
            collected = previous.Count > 0;
        }

        private void Add(Component comp)
        {
            if (comp != null) Add(comp.gameObject);
        }

        private void Add(GameObject go)
        {
            if (go != null && !previous.ContainsKey(go)) previous[go] = go.activeSelf;
        }

        /// <summary>Call every frame while the view is active.</summary>
        public void Apply()
        {
            if (!collected) Collect();
            foreach (KeyValuePair<GameObject, bool> kv in previous)
            {
                if (kv.Key != null && kv.Key.activeSelf) kv.Key.SetActive(false);
            }
        }

        public void Restore()
        {
            foreach (KeyValuePair<GameObject, bool> kv in previous)
            {
                try { if (kv.Key != null) kv.Key.SetActive(kv.Value); } catch { }
            }
            previous.Clear();
            collected = false;
        }
    }

    /// <summary>KSC-style overlay for a Space Center view: currency strip, title, facility bar, warp to next morning.</summary>
    public static class SpaceCenterHud
    {
        private static GUIStyle titleStyle, barStyle, currencyStyle, btnStyle;
        private static bool ready;
        private static double nextMorningUT = -1d;
        private static double nextMorningComputedAt = -1d;
        private static bool warping;

        private static void EnsureStyles()
        {
            if (ready) return;
            titleStyle = new GUIStyle(HighLogic.Skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            barStyle = new GUIStyle(HighLogic.Skin.box);
            currencyStyle = new GUIStyle(HighLogic.Skin.label) { fontSize = 13, fontStyle = FontStyle.Bold };
            btnStyle = new GUIStyle(HighLogic.Skin.button) { fontSize = 12 };
            ready = true;
        }

        public static void Reset()
        {
            nextMorningUT = -1d;
            nextMorningComputedAt = -1d;
            warping = false;
        }

        public static void Draw(OffworldCenter center, Vessel anchor)
        {
            EnsureStyles();
            GUI.skin = HighLogic.Skin;
            string title = center != null ? center.Name + " Space Center" : "Space Center";

            // --- top-left: currencies, like the stock widget --------------------------------------------------
            Game.Modes mode = HighLogic.CurrentGame != null ? HighLogic.CurrentGame.Mode : Game.Modes.SANDBOX;
            if (mode == Game.Modes.CAREER || mode == Game.Modes.SCIENCE_SANDBOX)
            {
                List<string> parts = new List<string>();
                if (mode == Game.Modes.CAREER && Funding.Instance != null) parts.Add("Funds " + Funding.Instance.Funds.ToString("N0"));
                if (ResearchAndDevelopment.Instance != null) parts.Add("Science " + ResearchAndDevelopment.Instance.Science.ToString("N1"));
                if (mode == Game.Modes.CAREER && Reputation.Instance != null) parts.Add("Rep " + Reputation.CurrentRep.ToString("N0"));
                string text = string.Join("    ", parts.ToArray());
                Vector2 size = currencyStyle.CalcSize(new GUIContent(text));
                GUI.Box(new Rect(8f, 8f, size.x + 24f, size.y + 12f), "", barStyle);
                GUI.Label(new Rect(20f, 14f, size.x + 10f, size.y + 4f), text, currencyStyle);
            }

            // --- bottom: title + facility bar -----------------------------------------------------------------
            float barW = Mathf.Min(Screen.width - 40f, 980f);
            float barH = 74f;
            Rect bar = new Rect((Screen.width - barW) / 2f, Screen.height - barH - 10f, barW, barH);
            GUI.Box(bar, "", barStyle);
            GUI.Label(new Rect(bar.x, bar.y + 4f, bar.width, 22f), title, titleStyle);

            GUILayout.BeginArea(new Rect(bar.x + 10f, bar.y + 30f, bar.width - 20f, 36f));
            GUILayout.BeginHorizontal();
            if (center != null)
            {
                if (GUILayout.Button("VAB", btnStyle)) SceneActions.OpenFacility(center, FacilityRole.VAB);
                if (GUILayout.Button("SPH", btnStyle)) SceneActions.OpenFacility(center, FacilityRole.SPH);
                if (GUILayout.Button("Tracking Station", btnStyle)) SceneActions.OpenFacility(center, FacilityRole.TrackingStation);
                if (GUILayout.Button("Astronaut Complex", btnStyle)) SceneActions.OpenFacility(center, FacilityRole.AstronautComplex);
                if (GUILayout.Button("R&D", btnStyle)) SceneActions.OpenFacility(center, FacilityRole.ResearchAndDevelopment);
                if (GUILayout.Button("Mission Control", btnStyle)) SceneActions.OpenFacility(center, FacilityRole.MissionControl);
                if (GUILayout.Button("Administration", btnStyle)) SceneActions.OpenFacility(center, FacilityRole.Administration);
            }
            GUILayout.FlexibleSpace();
            DrawNextMorning(anchor);
            if (GUILayout.Button("Bases", btnStyle, GUILayout.Width(64f)))
            {
                if (OffworldWindow.Instance != null) OffworldWindow.Instance.ShowBasePanel();
            }
            if (GUILayout.Button("KSC", btnStyle, GUILayout.Width(50f)))
            {
                GhostView.GoToRealSpaceCenter(FacilityRole.None);
            }
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        // ------------------------------------------------------------------ warp to next morning

        private static void DrawNextMorning(Vessel anchor)
        {
            if (anchor == null || anchor.mainBody == null) return;
            double now = Planetarium.GetUniversalTime();
            if (nextMorningComputedAt < 0d || now - nextMorningComputedAt > 30d || now > nextMorningUT)
            {
                nextMorningUT = ComputeNextMorning(anchor);
                nextMorningComputedAt = now;
            }
            if (warping && (TimeWarp.CurrentRate <= 1f && now >= nextMorningUT - 1d)) warping = false;

            if (warping)
            {
                if (GUILayout.Button("Stop warp", btnStyle, GUILayout.Width(140f)))
                {
                    TimeWarp.fetch.CancelAutoWarp(0, false);
                    TimeWarp.SetRate(0, true);
                    warping = false;
                }
                return;
            }
            GUI.enabled = nextMorningUT > now;
            string label = nextMorningUT > now ? "Warp to next morning" : "No sunrise soon";
            if (GUILayout.Button(label, btnStyle, GUILayout.Width(150f)) && nextMorningUT > now)
            {
                TimeWarp.fetch.WarpTo(nextMorningUT, 100000d, 1d);
                warping = true;
            }
            GUI.enabled = true;
        }

        /// <summary>
        /// Finds the next UT at which the sun rises about 5° above the horizon at the anchor, by rotating the
        /// local surface normal with the body's angular velocity and tracking the sun direction as the body moves.
        /// Returns -1 if there is no sunrise within two rotations (polar day/night, tidally locked facing sun, ...).
        /// </summary>
        private static double ComputeNextMorning(Vessel anchor)
        {
            try
            {
                CelestialBody body = anchor.mainBody;
                CelestialBody sun = Planetarium.fetch != null ? Planetarium.fetch.Sun : null;
                if (sun == null || body == sun) return -1d;
                if (!body.rotates || body.rotationPeriod == 0d) return -1d;

                double now = Planetarium.GetUniversalTime();
                Vector3d normalNow = (anchor.GetWorldPos3D() - body.position).normalized;
                Vector3d axis = body.angularVelocity.normalized;
                double omegaDeg = body.angularVelocity.magnitude * 180d / Math.PI; // deg per second
                double horizon = body.solarDayLength > 0d && body.solarDayLength < 1e9 ? body.solarDayLength * 2d : Math.Abs(body.rotationPeriod) * 2d;
                int steps = 1440;
                double dt = horizon / steps;
                double target = Math.Sin(5d * Math.PI / 180d);

                double prevElev = Elevation(body, sun, normalNow, axis, omegaDeg, now, 0d);
                for (int i = 1; i <= steps; i++)
                {
                    double t = i * dt;
                    double elev = Elevation(body, sun, normalNow, axis, omegaDeg, now, t);
                    if (prevElev < target && elev >= target)
                    {
                        // refine by bisection
                        double lo = t - dt, hi = t;
                        for (int k = 0; k < 20; k++)
                        {
                            double mid = (lo + hi) / 2d;
                            if (Elevation(body, sun, normalNow, axis, omegaDeg, now, mid) >= target) hi = mid; else lo = mid;
                        }
                        return now + hi;
                    }
                    prevElev = elev;
                }
            }
            catch (Exception e)
            {
                Log.Warn("Next morning: " + e.Message);
            }
            return -1d;
        }

        private static double Elevation(CelestialBody body, CelestialBody sun, Vector3d normalNow, Vector3d axis, double omegaDeg, double now, double dt)
        {
            Vector3d normal = QuaternionD.AngleAxis(omegaDeg * dt, axis) * normalNow;
            Vector3d bodyPos = body.orbit != null ? body.getPositionAtUT(now + dt) : body.position;
            Vector3d sunDir = (sun.position - bodyPos).normalized;
            return Vector3d.Dot(normal, sunDir);
        }
    }
}

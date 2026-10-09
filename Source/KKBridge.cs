using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace KolonySpaceCenters
{
    /// <summary>
    /// All direct contact with Kerbal Konstructs lives here. Every method that references a KK type
    /// is kept out-of-line and guarded, so the mod still loads (recovery markers only) if KK is absent.
    /// </summary>
    public static class KKBridge
    {
        public class SiteInfo
        {
            public string Name;
            public string BodyName;
            public double Latitude;
            public double Longitude;
            public double Altitude;
            public bool IsOpen;
            public bool IsStockKSC;
            public string Category;
            public string Description;
        }

        public class StaticInfo
        {
            public string UUID;
            public string Title;
            public string BodyName;
            public double Latitude;
            public double Longitude;
            public Vector3 Position;
            public bool HasLaunchSite;
            public bool Spawned;
        }

        /// <summary>Raised when the player clicks / hovers a KK building (static UUID).</summary>
        public static event Action<string> OnStaticClicked;
        public static event Action<string> OnStaticHoverEnter;
        public static event Action<string> OnStaticHoverExit;

        private static bool? _available;
        private static bool clickHooked;

        public static bool Available
        {
            get
            {
                if (_available == null)
                {
                    bool found = false;
                    foreach (AssemblyLoader.LoadedAssembly la in AssemblyLoader.loadedAssemblies)
                    {
                        if (la.name == "KerbalKonstructs") { found = true; break; }
                    }
                    _available = found;
                    Log.Info("Kerbal Konstructs " + (found ? "detected." : "not found; only free markers will be available."));
                }
                return _available.Value;
            }
        }

        public static List<SiteInfo> GetLaunchSites()
        {
            if (!Available) return new List<SiteInfo>();
            try
            {
                return GetLaunchSitesImpl();
            }
            catch (Exception e)
            {
                Log.Warn("Could not read KK launch sites: " + e.Message);
                return new List<SiteInfo>();
            }
        }

        public static bool IsSiteOpen(string siteName)
        {
            if (!Available) return true;
            try { return IsSiteOpenImpl(siteName); }
            catch { return true; }
        }

        /// <summary>Make KK pre-select this launch site the next time an editor opens.</summary>
        public static bool SetEditorLaunchSite(string siteName, bool sph)
        {
            if (!Available || string.IsNullOrEmpty(siteName)) return false;
            try { return SetEditorLaunchSiteImpl(siteName, sph); }
            catch (Exception e) { Log.Warn("SetEditorLaunchSite failed: " + e.Message); return false; }
        }

        public static Transform GetSiteTransform(string siteName)
        {
            if (!Available || string.IsNullOrEmpty(siteName)) return null;
            try { return GetSiteTransformImpl(siteName); }
            catch { return null; }
        }

        public static float GetSiteCameraRotation(string siteName)
        {
            if (!Available || string.IsNullOrEmpty(siteName)) return 90f;
            try { return GetSiteCameraRotationImpl(siteName); }
            catch { return 90f; }
        }

        /// <summary>Tell KK the Space Center camera has been moved so it resets it to the KSC when we stop relocating.</summary>
        public static void MarkSpaceCenterCamAltered(bool altered)
        {
            if (!Available) return;
            try { MarkSpaceCenterCamAlteredImpl(altered); }
            catch (Exception e) { Log.Warn("MarkSpaceCenterCamAltered failed: " + e.Message); }
        }

        /// <summary>Tell KK which body is "current" (it assumes the home world in the Space Center scene) and reload statics.</summary>
        public static void SwitchBody(CelestialBody body)
        {
            if (!Available || body == null) return;
            try { SwitchBodyImpl(body); }
            catch (Exception e) { Log.Warn("SwitchBody failed: " + e.Message); }
        }

        /// <summary>Ask KK to re-evaluate which statics are visible around the current camera/vessel.</summary>
        public static void RefreshStatics()
        {
            if (!Available) return;
            try { RefreshStaticsImpl(); }
            catch (Exception e) { Log.Warn("RefreshStatics failed: " + e.Message); }
        }

        /// <summary>All KK buildings on the given body within radius of a point.</summary>
        public static List<StaticInfo> GetStaticsNear(string bodyName, double lat, double lon, double radiusMeters)
        {
            if (!Available) return new List<StaticInfo>();
            try { return GetStaticsNearImpl(bodyName, lat, lon, radiusMeters); }
            catch (Exception e) { Log.Warn("GetStaticsNear failed: " + e.Message); return new List<StaticInfo>(); }
        }

        public static StaticInfo GetStatic(string uuid)
        {
            if (!Available || string.IsNullOrEmpty(uuid)) return null;
            try { return GetStaticImpl(uuid); }
            catch { return null; }
        }

        /// <summary>Hook KK's mouse events once; safe to call repeatedly.</summary>
        public static void EnsureClickHooks()
        {
            if (!Available || clickHooked) return;
            try
            {
                HookClicksImpl();
                clickHooked = true;
            }
            catch (Exception e) { Log.Warn("Could not hook KK click events: " + e.Message); }
        }

        public static void Highlight(string uuid, Color color)
        {
            if (!Available || string.IsNullOrEmpty(uuid)) return;
            try { HighlightImpl(uuid, color); } catch { }
        }

        public static void Unhighlight(string uuid)
        {
            if (!Available || string.IsNullOrEmpty(uuid)) return;
            try { HighlightImpl(uuid, Color.black); } catch { }
        }

        private static void RaiseClicked(string uuid) { if (OnStaticClicked != null) try { OnStaticClicked(uuid); } catch (Exception e) { Log.Warn("click handler: " + e.Message); } }
        private static void RaiseEnter(string uuid) { if (OnStaticHoverEnter != null) try { OnStaticHoverEnter(uuid); } catch (Exception e) { Log.Warn("hover handler: " + e.Message); } }
        private static void RaiseExit(string uuid) { if (OnStaticHoverExit != null) try { OnStaticHoverExit(uuid); } catch (Exception e) { Log.Warn("hover handler: " + e.Message); } }

        // ------------------------------------------------------------------ implementations (touch KK types)

        /// <summary>Reflection handles into KK internals; a nested class so they are only initialised when KK is present.</summary>
        private static class Refl
        {
            public static readonly FieldInfo modelField = typeof(KerbalKonstructs.Core.StaticInstance).GetField("model", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            public static FieldInfo modelTitleField;
            public static readonly MethodInfo highlightMethod = typeof(KerbalKonstructs.Core.StaticInstance).GetMethod("HighlightObject", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string TitleOf(KerbalKonstructs.Core.StaticInstance inst)
        {
            try
            {
                object model = Refl.modelField != null ? Refl.modelField.GetValue(inst) : null;
                if (model != null)
                {
                    if (Refl.modelTitleField == null) Refl.modelTitleField = model.GetType().GetField("title", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    string t = Refl.modelTitleField != null ? Refl.modelTitleField.GetValue(model) as string : null;
                    if (!string.IsNullOrEmpty(t)) return t;
                }
            }
            catch { }
            if (inst.gameObject != null && !string.IsNullOrEmpty(inst.gameObject.name)) return inst.gameObject.name;
            return inst.UUID;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static StaticInfo ToInfo(KerbalKonstructs.Core.StaticInstance inst)
        {
            StaticInfo si = new StaticInfo();
            si.UUID = inst.UUID;
            si.Title = TitleOf(inst);
            si.BodyName = inst.CelestialBody != null ? inst.CelestialBody.name : "";
            si.Latitude = inst.RefLatitude;
            si.Longitude = inst.RefLongitude;
            si.HasLaunchSite = inst.hasLauchSites;
            si.Spawned = inst.gameObject != null;
            try { si.Position = inst.position; } catch { }
            return si;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static List<StaticInfo> GetStaticsNearImpl(string bodyName, double lat, double lon, double radiusMeters)
        {
            List<StaticInfo> list = new List<StaticInfo>();
            KerbalKonstructs.Core.StaticInstance[] all = KerbalKonstructs.Core.StaticDatabase.GetAllStatics();
            if (all == null) return list;
            CelestialBody body = FlightGlobals.GetBodyByName(bodyName);
            if (body == null) return list;
            foreach (KerbalKonstructs.Core.StaticInstance inst in all)
            {
                if (inst == null || inst.CelestialBody == null || inst.CelestialBody.name != bodyName) continue;
                double d = KolonyCenter.GreatCircle(body.Radius, lat, lon, inst.RefLatitude, inst.RefLongitude);
                if (d > radiusMeters) continue;
                list.Add(ToInfo(inst));
            }
            return list;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static StaticInfo GetStaticImpl(string uuid)
        {
            KerbalKonstructs.Core.StaticInstance[] all = KerbalKonstructs.Core.StaticDatabase.GetAllStatics();
            if (all == null) return null;
            foreach (KerbalKonstructs.Core.StaticInstance inst in all)
            {
                if (inst != null && inst.UUID == uuid) return ToInfo(inst);
            }
            return null;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void HookClicksImpl()
        {
            KerbalKonstructs.API.RegisterOnStaticClicked(inst => { if (inst != null) RaiseClicked(inst.UUID); });
            KerbalKonstructs.API.RegisterOnStaticMouseEnter(inst => { if (inst != null) RaiseEnter(inst.UUID); });
            KerbalKonstructs.API.RegisterOnStaticMouseExit(inst => { if (inst != null) RaiseExit(inst.UUID); });
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void HighlightImpl(string uuid, Color color)
        {
            if (Refl.highlightMethod == null) return;
            KerbalKonstructs.Core.StaticInstance[] all = KerbalKonstructs.Core.StaticDatabase.GetAllStatics();
            if (all == null) return;
            foreach (KerbalKonstructs.Core.StaticInstance inst in all)
            {
                if (inst != null && inst.UUID == uuid && inst.gameObject != null)
                {
                    Refl.highlightMethod.Invoke(inst, new object[] { color });
                    return;
                }
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static List<SiteInfo> GetLaunchSitesImpl()
        {
            List<SiteInfo> result = new List<SiteInfo>();
            List<KerbalKonstructs.Core.KKLaunchSite> sites = KerbalKonstructs.Core.LaunchSiteManager.AllLaunchSites;
            if (sites == null) return result;
            CelestialBody home = FlightGlobals.GetHomeBody();
            foreach (KerbalKonstructs.Core.KKLaunchSite site in sites)
            {
                if (site == null || string.IsNullOrEmpty(site.LaunchSiteName)) continue;
                SiteInfo info = new SiteInfo();
                info.Name = site.LaunchSiteName;
                CelestialBody body = site.body;
                if (body == null && site.staticInstance != null) body = site.staticInstance.CelestialBody;
                info.BodyName = body != null ? body.name : "";
                if (site.staticInstance != null)
                {
                    info.Latitude = site.staticInstance.RefLatitude;
                    info.Longitude = site.staticInstance.RefLongitude;
                    if (body != null)
                    {
                        try { info.Altitude = body.GetAltitude(site.staticInstance.position); }
                        catch { info.Altitude = site.refAlt; }
                    }
                }
                else
                {
                    info.Latitude = site.refLat;
                    info.Longitude = site.refLon;
                    info.Altitude = site.refAlt;
                }
                bool open = true;
                try { open = site.isOpen; } catch { }
                info.IsOpen = open;
                info.Category = site.sitecategory.ToString();
                info.Description = site.LaunchSiteDescription;
                info.IsStockKSC = body != null && home != null && body == home
                    && (site.LaunchSiteName == "Runway" || site.LaunchSiteName == "LaunchPad" || site.staticInstance == null);
                result.Add(info);
            }
            return result;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool IsSiteOpenImpl(string siteName)
        {
            KerbalKonstructs.Core.KKLaunchSite site = KerbalKonstructs.Core.LaunchSiteManager.GetLaunchSiteByName(siteName);
            return site == null || site.isOpen;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool SetEditorLaunchSiteImpl(string siteName, bool sph)
        {
            KerbalKonstructs.Core.KKLaunchSite site = KerbalKonstructs.Core.LaunchSiteManager.GetLaunchSiteByName(siteName);
            if (site == null) return false;
            KerbalKonstructs.KerbalKonstructs kk = KerbalKonstructs.KerbalKonstructs.instance;
            if (kk == null) return false;
            kk.lastLaunchSiteUsed = siteName;
            if (sph) kk.defaultSPHlaunchsite = siteName; else kk.defaultVABlaunchsite = siteName;
            return true;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Transform GetSiteTransformImpl(string siteName)
        {
            KerbalKonstructs.Core.KKLaunchSite site = KerbalKonstructs.Core.LaunchSiteManager.GetLaunchSiteByName(siteName);
            if (site == null || site.staticInstance == null || site.staticInstance.gameObject == null) return null;
            return site.staticInstance.gameObject.transform;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static float GetSiteCameraRotationImpl(string siteName)
        {
            KerbalKonstructs.Core.KKLaunchSite site = KerbalKonstructs.Core.LaunchSiteManager.GetLaunchSiteByName(siteName);
            return site != null ? site.InitialCameraRotation : 90f;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void MarkSpaceCenterCamAlteredImpl(bool altered)
        {
            FieldInfo f = typeof(KerbalKonstructs.KerbalKonstructs).GetField("scCamWasAltered", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            if (f != null) f.SetValue(null, altered);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void SwitchBodyImpl(CelestialBody body)
        {
            KerbalKonstructs.KerbalKonstructs kk = KerbalKonstructs.KerbalKonstructs.instance;
            if (kk == null) return;
            FieldInfo f = typeof(KerbalKonstructs.KerbalKonstructs).GetField("currentBody", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (f != null) f.SetValue(kk, body);
            MethodInfo m = typeof(KerbalKonstructs.Core.StaticDatabase).GetMethod("OnBodyChanged", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            if (m != null) m.Invoke(null, new object[] { body });
            kk.UpdateCache();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void RefreshStaticsImpl()
        {
            KerbalKonstructs.KerbalKonstructs kk = KerbalKonstructs.KerbalKonstructs.instance;
            if (kk != null) kk.UpdateCache();
        }
    }
}

# Offworld Space Centers

Turn the bases you build with **Kerbal Konstructs** (on any planet or moon) into real space centers:
recover vessels with the stock recovery system when you land near them, open the VAB/SPH/Tracking Station
from the base, designate your own buildings as facilities, see your bases in the Tracking Station, and make
any base *the* Space Center that every "Space Center" button opens.

For KSP 1.12.x. Requires Kerbal Konstructs (the KSP-RO fork, 1.12.3 or later) and Harmony 2.
Licensed under the MIT License (see `LICENSE`).

## Install

With CKAN: install **Offworld Space Centers**; the dependencies come along.

Manually: download the latest zip from [SpaceDock](https://spacedock.info/mod/4662/Offworld%20Space%20Centers),
copy its `GameData/OffworldSpaceCenters` folder into your KSP `GameData` folder and make sure Kerbal Konstructs
and Harmony 2 (`000_Harmony`) are installed as well:

```
GameData/OffworldSpaceCenters/Plugins/OffworldSpaceCenters.dll
GameData/OffworldSpaceCenters/Parts/OffworldGhostAnchor.cfg
GameData/OffworldSpaceCenters/Parts/OffworldGhostAnchor.mu
GameData/OffworldSpaceCenters/Textures/icon.png
GameData/OffworldSpaceCenters/OffworldSpaceCenters.version
```

Upgrading from the mod's previous name, Kolony Space Centers: delete `GameData/KolonySpaceCenters` first. Your
flagged bases, facility assignments and selected Space Center are imported from the old save data automatically
the first time a save is loaded; only the difficulty settings go back to their defaults.

## How to use

### 1. Flag a base as a Space Center

Click the Offworld Space Centers toolbar button (dome with antenna) in Flight, the Tracking Station or the Space Center.
**Manage bases** lists every Kerbal Konstructs launch site grouped by body; press **Set as Space Center**.
Stock KSC pad/runway are hidden; tick "show Kerbin" for other KK sites on the home world. Each flagged base has
a recovery **radius** (default 10 km, adjustable per base), an **Enabled** toggle and a **Remove** button.

For bases made of parts, or statics without a launch site, land there and use **Drop a marker at this vessel**.

Every change is written to `persistent.sfs` immediately (outside flight, or in flight whenever the game is in a
saveable state), so reloading a scene never loses it.

### 2. Recover vessels there

Land or splash down inside the radius, on any body. The stock altimeter **Recover Vessel** button unlocks
exactly as at the KSC (come to a stop first), and the Tracking Station **Recover** button works too. Recovery pays
100% inside the radius (configurable) and the summary says it was recovered at your base.

### 3. Bases in the Tracking Station

Every flagged base gets the same map icon the stock launch sites use (the Dessert Airfield / Woomerang glyph;
runway glyph for SPH-type KK sites), in the Tracking Station and in map view, filtered by the stock
"launch sites" map filter. Hover for details, click to open the Offworld Space Centers window on that base. A navigation
waypoint is created as well, so you can target a base from flight.

### 4. Make a base your Space Center

**Space Center view** tab: pick the stock KSC or any flagged base. The chosen base is what *every* "Space
Center" button opens (flight altimeter, editor exit, Tracking Station) as long as "Space Center button opens
selected base" is on in difficulty settings. With the anchor-view fallback, pending recoveries still go to the KSC
so the stock summary works.

* **Base on the home world**: the Space Center scene opens with the camera parked at the base.
* **Base on another body**: the real Space Center scene runs on that body. Stock sets the scene up from three
  references (which PQS to activate, which body is the rotating reference frame, where the world origin goes);
  the mod points them at the target body and at an anchor transform placed exactly at your base (a plain child
  of that body's terrain object, the same way the stock camera pivot hangs under Kerbin's), lets stock do its
  normal setup, and puts them back the moment the next scene change is requested. You get the stock Space Center
  UI, camera and warp-to-morning, that body's terrain and sky, and your Kerbal Konstructs buildings where the KSC
  would be. Astronaut Complex, R&D, Mission Control and Administration open right there. The stock "Fly / Recover"
  vessel markers show the craft parked at the base (within the base radius, at least 5 km) instead of the ones
  landed on Kerbin. This is new and experimental; if a visual mod misbehaves, turn off "Off-world bases use the
  real Space Center scene" in difficulty settings to fall back to the anchor view (an invisible anchor part in the
  flight scene, flight UI hidden, KSC-style overlay).
  Resuming the save from the main menu, quickloading and recovering a vessel all open the selected base directly
  (the selection is read from the save before the scene initialises); the recovery summary shows up there.

"Kerbin Space Center (KSC)" / "Go to stock KSC" always takes you to the real KSC for that one visit; the base
stays selected, so the next Space Center button returns to it. Tick "Stock ... Space Center (KSC)" in the Space
Center view tab to make the KSC the default again.

Astronaut Complex, R&D, Mission Control and Administration are stock screens that only exist inside the KSC
scene; from an off-world Space Center those buttons jump to the KSC and open the screen for you.

### 5. Designate buildings as facilities

Kerbal Konstructs has no concept of "this hangar is my VAB", so the mod adds one. In **Manage bases**, press
**Buildings** on a base. For each role (VAB, SPH, Tracking Station, Astronaut Complex, R&D, Mission Control,
Administration) you can:

* **Click**: while you are in that base's Space Center view (or landed inside it), the next building you click in the
  scene gets the role. Esc cancels.
* **List**: pick from every KK building inside the base radius, sorted by name with distance.
* **Clear**: remove the assignment.

Designated buildings glow when hovered, show their role as a tooltip, and open the facility when clicked, in the
home-world Space Center scene as well as in off-world Space Center views. A role can only be held by one building.

### 6. Stations and ships as Space Centers

Any vessel can be a Space Center that moves with it. In **Manage bases** → *Stations and ships*, flag the active
vessel in flight, or pick one from the list in the Tracking Station / Space Center (stations and bases by default,
tick "show all vessel types" for ships and landers). A flagged vessel then behaves like any other center:

* Craft that come within its radius and match velocity (under 1 m/s relative) can be recovered with the stock
  Recover button, at full value. Undock first: a docked craft is part of the station, and the station itself is
  never recoverable through its own center.
* Selecting it as the Space Center makes every "Space Center" button open the station view: flight focused on
  the station with the UI hidden, controls locked and a camera framed to the station's size. "Leave Space Center
  mode and fly this vessel" hands control back.
* **Parts** can be designated as facilities exactly like KK buildings (Click or List); they glow on hover and open
  the facility when clicked.
* There is no launch pad in orbit: VAB/SPH open the editor and new craft launch from whatever launch site is
  selected there. Stations do not get an extra map icon, since the vessel already has one.

If the station is destroyed or recovered, its center shows "Vessel no longer exists"; remove it from the list.

## Difficulty options

Settings → Difficulty → **Offworld Space Centers**:

| Option | Default | Meaning |
|---|---|---|
| Default recovery radius (km) | 10 | Radius given to newly flagged bases |
| Enable recovery at flagged bases | on | Master switch for the recovery patches |
| Full recovery value inside radius | on | 100% value inside the radius; off = stock distance formula |
| Require the KK launch site to be open | off | Career: closed/unpurchased KK sites do not recover |
| Relocate Space Center camera (experimental) | on | Home-world camera relocation |
| Space Center button opens selected base | on | Redirect every Space Center button to the selected off-world base |
| Save immediately when bases change | on | Write persistent.sfs on every flag/unflag/selection change |
| Show range messages | on | Screen message when entering/leaving a recovery zone |
| Open base panel on arrival | on | Auto-open the window when a Space Center view opens |

## What it patches

Harmony patches, applied independently:

* `Vessel.IsRecoverable` getter: stock is `LandedOrSplashed && mainBody.isHomeWorld`; a postfix adds "or inside an
  enabled flagged base" (landed for ground bases, any situation within a station's radius).
* `VesselRecovery.OnVesselRecovered`: stock pays 100% when `landedAt` names a pad and otherwise lerps 98% → 10%
  by distance from `SpaceCenter.Instance`. A prefix temporarily makes the proto vessel look pad-recovered and names
  your base as the location; a finalizer restores both.
* `KSP.UI.Screens.AltimeterSliderButtons.UpdateForVessel`: the flight Recover button has its own `isHomeWorld`
  check; a postfix runs an equivalent unlock coroutine for landed vessels inside a flagged radius.
* `PSystemSetup.SetSpaceCentre` / `PSystemSetup.OnLevelLoaded` / `SpaceCenterCamera2.Start`: redirect the real
  Space Center scene to the selected off-world base (see above). The home-world references are restored on every
  `onGameSceneLoadRequested`, so a Space Center to Space Center reload always starts clean.
* `KSP.UI.Screens.KSCVesselMarkers.SpawnVesselMarkers`: in an off-world Space Center scene only, the markers are
  created for the vessels parked at the base (stock would anchor them to craft landed on Kerbin, far outside the
  view); the stock prefab, buttons and dismiss handler are reused.
* `HighLogic.LoadScene`: with the anchor-view fallback, a SPACECENTER load for a selected off-world base becomes
  "open that base's anchor view" (never while a recovery is pending).
* `Vessel.GoOffRails`: the anchor part (fallback mode only) never leaves rails, so it has no physics and zero
  surface speed.

Everything else uses public KSP and KK APIs (`MapNode` for icons, `API.RegisterOnStaticClicked/MouseEnter/MouseExit`
for buildings, `LaunchSiteManager` / `StaticDatabase` for data). All KK calls live in `KKBridge` and are guarded.

## Known limitations

* Astronaut Complex, R&D, Mission Control and Administration cannot be shown anywhere but the KSC scene.
* Recovery from flight always ends in the stock KSC scene with the summary dialog (stock behaviour).
* The off-world anchor vessel is briefly visible in the Tracking Station list if you look very quickly; it is
  removed automatically.
* Free markers do not change which launch site the editor uses; only KK launch sites can be pre-selected.

## Building from source

`Source/` contains a .NET Framework 4.8 project. Set `KSPDIR` (defaults to your Steam path) and run
`dotnet build` or open it in Visual Studio/Rider; the DLL is copied to `GameData/OffworldSpaceCenters/Plugins`.
On Linux/WSL with Mono: `KSPDIR="/path/to/KSP" ./build.sh`.

## Releasing (CKAN)

CKAN indexes this mod from its [SpaceDock page](https://spacedock.info/mod/4662/Offworld%20Space%20Centers), not
from GitHub. To publish a new version: bump `VERSION` in `GameData/OffworldSpaceCenters/OffworldSpaceCenters.version`
and in `Source/AssemblyInfo.cs`, build, zip `GameData/OffworldSpaceCenters` together with `LICENSE` and `README.md`
as `OffworldSpaceCenters-<version>.zip` (with `GameData` at the top level of the zip), and upload it as a new
version on SpaceDock. CKAN picks it up automatically and reads the KSP compatibility from the `.version` file.

Keep the folder named `OffworldSpaceCenters` so it matches the CKAN identifier, and do not bundle Harmony or
Kerbal Konstructs in the zip; CKAN installs them from the `depends` list. `OffworldSpaceCenters.netkan` is a
reference copy of the metadata in the [NetKAN repository](https://github.com/KSP-CKAN/NetKAN). CKAN does not
read it from here, so dependency or tag changes need a pull request there.

## Uninstall

Delete `GameData/OffworldSpaceCenters`. Saves keep a harmless `OffworldScenario` node that KSP ignores. If you remove
the mod while an off-world Space Center view is the current scene, delete the "KSC Anchor" vessel in the Tracking
Station.

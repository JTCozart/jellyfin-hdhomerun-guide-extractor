# HDHomeRun EPG for Jellyfin

[![Build Plugin](https://github.com/JTCozart/jellyfin-hdhomerun-guide-extractor/actions/workflows/build.yaml/badge.svg)](https://github.com/JTCozart/jellyfin-hdhomerun-guide-extractor/actions/workflows/build.yaml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](./LICENSE)

A Jellyfin server plugin that generates an **auto-refreshing XMLTV guide** from a HDHomeRun
device's own EPG, with channel ids pre-mapped to the device's tuner lineup — no manual channel
mapping, no external scripts, no cron jobs.

## How it works

Jellyfin's stock web client only offers two guide-provider types out of the box ("XMLTV" and
"Schedules Direct") — there's no way for a plugin to add a genuinely new entry to that dropdown
without shipping a modified web client. Instead, this plugin polls the HDHomeRun device
directly (mirroring [HDHomeRunEPG-to-XmlTv](https://github.com/IncubusVictim/HDHomeRunEPG-to-XmlTv)'s
approach) and writes a ready-to-use XMLTV file on a schedule. You point Jellyfin's **existing,
built-in XMLTV guide provider** at that file once, and this plugin keeps it current from then
on.

1. Discovers the device's auth token via `/discover.json` and its channel lineup via
   `/lineup.json`.
2. Fetches the device's EPG from SiliconDust's cloud XMLTV API.
3. Remaps the feed's internal channel ids to the tuner's own guide numbers by matching each
   lineup entry's `display-name`, so channels line up with your HDHomeRun tuner without any
   manual mapping.
4. Writes the result to `<config>/data/hdhrepg/epg.xml` on a configurable interval (default 12
   hours).

## Setup

1. Install the plugin (below) and restart Jellyfin.
2. Open **Dashboard → Plugins → HDHomeRun EPG**, set your device's host (default
   `hdhomerun.local`), save, then click **Refresh now**.
3. Once status shows success, go to **Dashboard → Live TV → Guide Data Providers → Add**,
   choose **XMLTV**, and paste in the file path shown on the plugin's page.

From then on the guide refreshes itself; no further steps required.

## Installing (recommended: plugin repository)

Add this repository in Jellyfin to install and get automatic updates from the catalog:

1. **Dashboard → Plugins → Repositories → ＋** and add the manifest URL:
   ```
   https://raw.githubusercontent.com/JTCozart/jellyfin-hdhomerun-guide-extractor/master/manifest.json
   ```
2. **Catalog →** find **HDHomeRun EPG** (Live TV) and click **Install**.
3. Restart Jellyfin, then open **HDHomeRun EPG** from the Dashboard menu.

The full install guide lives at
**https://jtcozart.github.io/jellyfin-hdhomerun-guide-extractor/**.

## Installing manually

1. Build (`dotnet build -c Release` — the DLL lands in
   `Jellyfin.Plugin.HdhrEpg/bin/Release/net9.0/`), or download the `.zip` from
   [Releases](https://github.com/JTCozart/jellyfin-hdhomerun-guide-extractor/releases).
2. Copy `Jellyfin.Plugin.HdhrEpg.dll` into a `HDHomeRunEPG` folder under your server's `plugins`
   directory.
3. Restart Jellyfin and open **Dashboard → Plugins → HDHomeRun EPG**.

## Packaging / releases

Continuous integration builds the plugin on every push/PR via
[`.github/workflows/build.yaml`](.github/workflows/build.yaml), which delegates to Jellyfin's
shared meta-plugins workflow.

To cut a release, push a **timestamp tag** of the form `vYYYYMMDD.HHMM`:

```sh
git tag v$(date +%Y%m%d.%H%M)
git push origin --tags
```

[`.github/workflows/release.yaml`](.github/workflows/release.yaml) maps the tag to a 4-part
Jellyfin plugin version, packages with [JPRM](https://github.com/oddstr13/jellyfin-plugin-repository-manager),
creates a GitHub Release, and updates `manifest.json`.

## Requirements

- Jellyfin **10.11.x** (ABI `10.11.0.0`)
- .NET 9 SDK to build
- A HDHomeRun device reachable on the local network

## License

[MIT](./LICENSE)

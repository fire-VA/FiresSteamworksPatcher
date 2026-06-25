* v1.1.1 packaging fix — installs to BepInEx/patchers/ correctly
  - Fixed the Thunderstore package layout: the patcher DLL now sits inside a `patchers/` folder, so mod managers install it to `BepInEx/patchers/` instead of `BepInEx/plugins/`. The 1.1.0 package shipped the DLL at the zip root, which r2modman/Thunderstore route to `plugins/` — where a preloader patcher never runs.
  - No code change: identical client + server patcher to 1.1.0, just packaged correctly.
* v1.1.0
  - The patcher now runs on **clients as well as dedicated servers** — anywhere FiresGhettoNetworking is installed. The previous dedicated-server-only process gate is gone; the FGN-presence check is the only remaining gate.
  - Why the client matters now: FGN's receive-buffer settings apply on the client too. A client needs matching receive headroom to keep up with a high-throughput server (HyperBoost / high AutoTune tiers push well past vanilla rates), and the patched enum members are what let FGN enlarge the client's receive buffers. Without the patcher those settings cap at Steam's defaults instead of erroring.
  - No change to what gets patched: same recv-buffer enum members and the same ZDOMan.SendZDOs queue-cap raise (10240 -> 102400) as 1.0.0.

* v1.0.0 first release
  - Adds the missing Steamworks recv-buffer enum members (RecvBufferSize / RecvBufferMessages / RecvMaxMessageSize / RecvMaxSegmentsPerPacket) to Steamworks.ESteamNetworkingConfigValue at preload time, so FGN can wire its recv-buffer config knobs without crashing on missing enum values.
  - Raises ZDOMan.SendZDOs outbound queue cap from 10240 to 102400 bytes (10x) by rewriting the constant in IL. Lets FGN's send-rate tier presets actually push more data per tick.
  - Dedicated-server gate: process name has to contain "server" (matches valheim_server.exe / valheim_server.x86_64 / user-renamed variants) or the patcher exits early and touches nothing. Both patches only affect server-side network behavior so a client install would be wasted IL rewrites and the gate keeps the impact strictly where it's intended.
  - FGN-presence gate: if no DLL matching `*GhettoNetwork*.dll` is found under `BepInEx/plugins/`, the patcher exits early. Both gates are checked in TargetDLLs (so BepInEx never even loads Cecil) and again defensively in Patch().

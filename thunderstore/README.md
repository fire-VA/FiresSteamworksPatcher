# FiresSteamworksPatcher

**Dedicated-server-side** BepInEx preloader patcher that makes two surgical changes to Valheim's bundled assemblies, so [FiresGhettoNetworking](https://thunderstore.io/c/valheim/p/VerdantsAscent/FiresGhettoNetworking/) can use the Steam networking knobs and ZDO queue sizes it ships configs for.

This package only does anything when **all three** of these are true on the machine:

1. The process is a dedicated Valheim server (`valheim_server.exe` / `valheim_server.x86_64` — checked via process name).
2. **FiresGhettoNetworking** is also installed (a `*GhettoNetwork*.dll` is present anywhere under `BepInEx/plugins/`).
3. BepInEx loads the patcher at preload (i.e. the DLL is in `BepInEx/patchers/`).

Anything else and the patcher exits with a single log line and touches nothing. Don't install this on a client expecting a performance bump — there isn't one. Don't install this on a server without FGN expecting one — there isn't one there either.

> Need help, found a bug, want a feature? **https://discord.gg/H9uKGcAujs**

---

## What it patches

| Target | What it does |
|--------|--------------|
| `Steamworks.ESteamNetworkingConfigValue` (in `com.rlabrecque.steamworks.net.dll`) | Adds four missing enum literals — `k_ESteamNetworkingConfig_RecvBufferSize` (47), `RecvBufferMessages` (48), `RecvMaxMessageSize` (49), `RecvMaxSegmentsPerPacket` (50). Valheim ships an older Steamworks SDK that doesn't expose these; without them, FGN's server-side recv-buffer config knobs would throw `MissingFieldException` when it tries to look them up via reflection. |
| `ZDOMan.SendZDOs` (in `assembly_valheim.dll`) | Locates the outbound send-queue cap constant (10240 bytes in vanilla) near a `GetSendQueueSize` call and rewrites it to 102400 (10×). FGN's per-client send-rate tier presets can configure Steam to push 50–200 KB/s outbound; the vanilla 10 KB queue caps that ceiling regardless of what Steam will accept. This bump lets the queue actually hold a frame of high-tier traffic. |

Both edits are done via Cecil at preload time — BEFORE Harmony exists, BEFORE any plugin Awakes. There's no Harmony-equivalent for either patch:

- The Steamworks enum members are missing from the **types Valheim ships**, so no Harmony patch could surface them — you can't `__instance.SomeField = …` if the field doesn't exist in the loaded assembly.
- The ZDO queue cap is a const-int load (`ldc.i4`) inside an IL branch. Harmony can transpile that, but the transpiler IL search and re-emit is heavier than rewriting one constant at preload.

---

## Why server-only

The dedicated server is the heavy outbound sender — it pushes ZDO updates to every connected peer every tick, configures per-connection Steam sockets to each of them, and is the bottleneck during initial-sync floods. Both patches target exactly that side:

- **ZDOMan.SendZDOs queue cap** — on a client, `SendZDOs` runs but only pushes ZDOs the client owns (their player character, dropped items briefly, sometimes a tamed companion). That traffic is well under the 10 KB vanilla cap, so raising the cap on a client is a no-op.
- **Steamworks recv-buffer enums** — FGN's recv-buffer configuration is server-side; it applies to the server's accept-from-peer sockets. A client only has one socket (to the server) and configures it from the client side via different code paths that don't go through these enum values.

The patcher checks `Process.GetCurrentProcess().ProcessName` for the substring "server" (case-insensitive). Catches `valheim_server.exe`, `valheim_server.x86_64`, and any user-renamed variant that follows the naming convention. Fail-closed — if we can't read the process name for any reason, we treat it as "not a server" and skip patching.

## FGN gate

Even on the server, the patches only do something useful when FGN is installed and reading them. So the second gate scans `BepInEx/plugins/` recursively for any DLL whose name matches `*GhettoNetwork*.dll`. If nothing matches:

1. `TargetDLLs` returns an empty list, so BepInEx skips loading Cecil for Steamworks / assembly_valheim entirely.
2. Defensively, `Patch()` also short-circuits if it's somehow called anyway.
3. A single log line is written: `FiresGhettoNetworking not detected … FiresSteamworksPatcher will not patch any assemblies.`

This gate exists because the Steamworks enum additions are inert without something reading them, and the queue cap raise is real but small on its own — pairing it with FGN's send-rate tier presets is where the actual win lives. Shipping the patcher to a non-FGN server would either be no-op (best case) or quietly improve other networking mods' headroom (worst case, and the reason this gate exists).

Both gates are evaluated once per process and cached. Cost is one `Process.GetCurrentProcess()` and one `Directory.GetFiles(... AllDirectories)` at startup, then nothing.

---

## Installation

1. On the **dedicated server**: install BepInEx — the [BepInExPack_Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/) on Thunderstore is the standard one.
2. On the **dedicated server**: install [FiresGhettoNetworking](https://thunderstore.io/c/valheim/p/VerdantsAscent/FiresGhettoNetworking/) — this patcher does nothing without it.
3. On the **dedicated server**: install this package. The `patchers/` folder from the zip drops into `BepInEx/` so the DLL lands at `BepInEx/patchers/<author>-FiresSteamworksPatcher/patchers/FiresSteamworksPatcher.dll`. r2modman / Thunderstore Mod Manager handle this automatically.
4. Restart the server.

Preloader patchers don't have a config file — there's nothing to tune. Either both gates pass and the two patches land, or one of them fails and the patcher idles.

### Where this needs to be installed

| Side | Needs the patcher? |
|------|:------------------:|
| Dedicated server | ✓ |
| Client | — (runtime-gated to no-op even if installed) |
| Listen-server host (player hosting via the in-game menu) | — (process is `valheim.exe`, not `valheim_server.exe` — gate treats as client) |

If you run a listen-server (host through the game's in-game menu, not the standalone dedicated-server binary), you can still benefit from FGN itself, but this patcher won't apply because the process name doesn't match.

---

## Compatibility

- **FiresGhettoNetworking** — designed for. If FGN is installed on the server, the patcher activates. If FGN isn't, the patcher is a no-op.
- **BetterNetworking / Serverside Simulations / any other networking overhaul** — the FGN gate ensures we won't accidentally improve them. If you uninstall FGN to try another mod, this patcher will detect it's gone and stop applying its patches on the next launch.
- **Other preloader patchers** — coexists. Cecil load order is deterministic (alphabetical by patcher folder). Our edits are additive (one new enum member set, one constant rewrite) and don't conflict with anything I'm aware of.

## License

MIT — see `LICENSE`.

## Issues / questions

https://discord.gg/H9uKGcAujs

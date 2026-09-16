# FiresSteamworksPatcher

**Updated for Valheim 1.0.**

BepInEx preloader patcher that makes two surgical changes to Valheim's bundled assemblies, so [FiresGhettoNetworking](https://thunderstore.io/c/valheim/p/VerdantsAscent/FiresGhettoNetworking/) can use the Steam networking knobs and ZDO queue sizes it ships configs for. **Install it wherever FGN runs — the dedicated server and every client.**

> **FiresGhettoNetworking is required, but not listed as a dependency.** Earlier versions of this package listed
> FGN as a dependency, which pinned a specific FGN version: every FGN update meant a new patcher release, and an
> outdated pin could pull in an old FGN that no longer works on the current game. The dependency was removed so the
> patcher never has to change when FGN does. **You still need FGN installed for this patcher to do anything** -
> without it the patcher writes one log line and patches nothing.

This package only does anything when **both** of these are true on the machine:

1. **FiresGhettoNetworking** is installed (a `*GhettoNetwork*.dll` is present anywhere under `BepInEx/plugins/`).
2. BepInEx loads the patcher at preload (i.e. the DLL is in `BepInEx/patchers/`).

If FGN isn't present, the patcher writes a single log line and touches nothing — it has no effect of its own and no config file to tune.

> Need help, found a bug, want a feature? **https://discord.gg/H9uKGcAujs**

---

## What it patches

| Target | What it does |
|--------|--------------|
| `Steamworks.ESteamNetworkingConfigValue` (in `com.rlabrecque.steamworks.net.dll`) | Adds four missing enum literals — `k_ESteamNetworkingConfig_RecvBufferSize` (47), `RecvBufferMessages` (48), `RecvMaxMessageSize` (49), `RecvMaxSegmentsPerPacket` (50). Valheim ships an older Steamworks SDK that doesn't expose these; without them, FGN's recv-buffer config knobs would throw `MissingFieldException` when it looks them up via reflection. |
| `ZDOMan.SendZDOs` (in `assembly_valheim.dll`) | Locates the outbound send-queue cap constant (10240 bytes in vanilla) near a `GetSendQueueSize` call and rewrites it to 102400 (10×). FGN's per-peer send-rate tiers can configure Steam to push far past vanilla rates; the 10 KB queue caps that ceiling regardless of what Steam will accept. This bump lets the queue actually hold a frame of high-tier traffic. |

Both edits are done via Cecil at preload time — BEFORE Harmony exists, BEFORE any plugin Awakes. There's no Harmony-equivalent for either patch:

- The Steamworks enum members are missing from the **types Valheim ships**, so no Harmony patch could surface them — you can't `__instance.SomeField = …` if the field doesn't exist in the loaded assembly.
- The ZDO queue cap is a const-int load (`ldc.i4`) inside an IL branch. Harmony can transpile that, but the transpiler IL search and re-emit is heavier than rewriting one constant at preload.

---

## Where it helps — server *and* client

Earlier builds gated this patcher to dedicated servers only. As of **1.1.0** it runs anywhere FGN is installed, because both edits pull their weight on the client too:

- **Dedicated server** — the heavy outbound sender: it pushes ZDO updates to every peer each tick and configures a Steam socket per connection. The recv-buffer enums size its accept-from-peer receive buffers, and the `SendZDOs` queue cap raises its outbound ceiling during initial-sync floods.
- **Client** — a client receiving a high-throughput server (FGN's HyperBoost or high AutoTune tiers push well past vanilla rates) needs matching receive headroom or it bottlenecks on ingest. The recv-buffer enums are what let FGN enlarge the client's receive buffers to keep up. The `SendZDOs` queue cap is a smaller win client-side — a client sends comparatively little — but it's harmless and applies in the cases where a client does own and push more.

Without this patcher, FGN still runs: its recv-buffer settings simply cap at Steam's defaults instead of erroring. The patcher is what unlocks the full receive ceiling on both ends.

## FGN gate

The patcher scans `BepInEx/plugins/` recursively for any DLL whose name matches `*GhettoNetwork*.dll`. If nothing matches:

1. `TargetDLLs` returns an empty list, so BepInEx never loads Cecil for Steamworks / assembly_valheim.
2. Defensively, `Patch()` also short-circuits if it's somehow called anyway.
3. A single log line is written: `FiresGhettoNetworking not detected … will not patch any assemblies.`

This gate exists because the enum additions are inert without FGN reading them, and the queue-cap raise is small on its own — the win comes from pairing both with FGN's send-rate tiers. Shipping the patcher to a non-FGN install would be a no-op at best, so the gate keeps it from quietly altering other networking mods' headroom.

The gate is evaluated once per process and cached — one `Directory.GetFiles(... AllDirectories)` at startup, then nothing.

---

## Installation

Install on **every machine running FGN** — the dedicated server and each client:

1. Install BepInEx — the [BepInExPack_Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/) on Thunderstore is the standard one.
2. Install [FiresGhettoNetworking](https://thunderstore.io/c/valheim/p/VerdantsAscent/FiresGhettoNetworking/) yourself — mod managers will not add it for you, because it is deliberately not a listed dependency, and this patcher does nothing without it.
3. Install this package. The `patchers/` folder from the zip drops into `BepInEx/` so the DLL lands at `BepInEx/patchers/<author>-FiresSteamworksPatcher/patchers/FiresSteamworksPatcher.dll`. r2modman / Thunderstore Mod Manager handle this automatically.
4. Restart the game / server.

Preloader patchers have no config file — there's nothing to tune. Either FGN is present and the two patches land, or it isn't and the patcher idles.

### Where to install

| Side | Install the patcher? |
|------|----------------------|
| Dedicated server | ✓ — sizes accept-from-peer recv buffers + raises the outbound queue cap |
| Client | ✓ — sizes the client's recv buffers to keep up with a high-throughput server |
| Listen-server host (hosting via the in-game menu) | ✓ — acts as both server and client; install it like any client |

---

## Compatibility

- **FiresGhettoNetworking** — designed for. If FGN is installed, the patcher activates; if not, it's a no-op.
- **BetterNetworking / Serverside Simulations / any other networking overhaul** — the FGN gate ensures we won't accidentally alter them. If you uninstall FGN to try another mod, this patcher detects it's gone and stops patching on the next launch.
- **Other preloader patchers** — coexists. Cecil load order is deterministic (alphabetical by patcher folder). Our edits are additive (one new enum member set, one constant rewrite) and don't conflict with anything I'm aware of.

## License

MIT — see `LICENSE`.

## Issues / questions

https://discord.gg/H9uKGcAujs

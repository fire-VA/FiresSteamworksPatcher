using System.Reflection;
using System.Runtime.InteropServices;

[assembly: AssemblyTitle("FiresSteamworksPatcher")]
[assembly: AssemblyDescription("BepInEx preloader patcher for FiresGhettoNetworking. (1) Adds the missing Steam SDK 1.51+ recv-buffer enum members to Valheim's bundled com.rlabrecque.steamworks.net.dll so existing SetConfigValue paths can target them. (2) Bumps the ZDOMan.SendZDOs queue cap from vanilla 10240 to 102400 (10x) for more per-peer ZDO flush headroom. Runs on both dedicated servers and clients wherever FGN is installed.")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("")]
[assembly: AssemblyProduct("FiresSteamworksPatcher")]
[assembly: AssemblyCopyright("Copyright ©  2026")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]

[assembly: ComVisible(false)]

[assembly: Guid("ee0f80de-6bd2-4a52-8997-bd60dce71b00")]

[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Collections.Generic;

namespace FiresSteamworksPatcher
{
    public static class FiresSteamworksPatcher
    {
        private const int ZdoSendQueueCapBytes = 102400;
        private const int VanillaZdoSendQueueCapBytes = 10240;
        private const int QueueSizeCallSearchRadius = 15;

        private const string SteamworksConfigEnumFullName = "Steamworks.ESteamNetworkingConfigValue";
        private const string ZdoManTypeName = "ZDOMan";
        private const string SendZdosMethodName = "SendZDOs";
        private const string GetSendQueueSizeMethodName = "GetSendQueueSize";

        // Pattern matched against plugin DLL filenames in BepInEx/plugins/. The
        // patcher will only run if at least one DLL whose name contains
        // "GhettoNetwork" (case-insensitive on Windows) is present. Catches the
        // historic deployment names — VAGhettoNetworking.dll today,
        // FiresGhettoNetworking.dll on some forks, plain GhettoNetworking.dll
        // if the user has manually renamed.
        private const string GhettoNetworkingDllGlob = "*GhettoNetwork*.dll";

        private static readonly ManualLogSource Log =
            Logger.CreateLogSource("FiresSteamworksPatcher");

        private static readonly (string Name, int Value)[] RecvBufferEnumMembers =
        {
            ("k_ESteamNetworkingConfig_RecvBufferSize",            47),
            ("k_ESteamNetworkingConfig_RecvBufferMessages",        48),
            ("k_ESteamNetworkingConfig_RecvMaxMessageSize",        49),
            ("k_ESteamNetworkingConfig_RecvMaxSegmentsPerPacket",  50),
        };

        // Cached one-shot checks. Each is nullable so we can distinguish
        // "not yet checked" from "checked and false."
        private static bool? _ghettoNetworkingPresent;
        private static bool? _isDedicatedServer;

        // BepInEx evaluates TargetDLLs once during patcher discovery. We return
        // an empty enumeration when either gate fails so BepInEx never even
        // loads Cecil for our target assemblies. Patch() is also defensively
        // gated for the rare case where BepInEx caches the pre-detection
        // result and calls us anyway.
        public static IEnumerable<string> TargetDLLs
        {
            get
            {
                if (!IsDedicatedServer())
                {
                    Log.LogInfo(
                        "Not running as a dedicated server — FiresSteamworksPatcher will not " +
                        "patch any assemblies. Both patches (Steam recv-buffer enum members and " +
                        "ZDOMan.SendZDOs queue cap) only affect server-side network behavior, " +
                        "so the patcher is no-op on client installs even when FGN is present.");
                    return Array.Empty<string>();
                }

                if (!IsGhettoNetworkingInstalled())
                {
                    Log.LogInfo(
                        "FiresGhettoNetworking not detected in BepInEx/plugins/ — " +
                        "FiresSteamworksPatcher will not patch any assemblies. This patcher " +
                        "exists to support FiresGhettoNetworking's send-buffer + queue-cap " +
                        "features and has no other effect; without FGN there's nothing to do.");
                    return Array.Empty<string>();
                }

                return new[]
                {
                    "com.rlabrecque.steamworks.net.dll",
                    "assembly_valheim.dll"
                };
            }
        }

        public static void Patch(AssemblyDefinition assembly)
        {
            var assemblyName = assembly.Name?.Name ?? "<unknown>";

            // Defensive second gates. TargetDLLs already filters us out when we
            // aren't a server or FGN is missing, but a future BepInEx that
            // caches the pre-detection result more aggressively could in
            // principle still hand us an assembly. Bail explicitly so we never
            // silently improve a client install or a non-FGN server install.
            if (!IsDedicatedServer())
            {
                Log.LogInfo($"{assemblyName}: not a dedicated server, leaving assembly untouched.");
                return;
            }
            if (!IsGhettoNetworkingInstalled())
            {
                Log.LogInfo($"{assemblyName}: FGN not present, leaving assembly untouched.");
                return;
            }

            try
            {
                var module = assembly.MainModule;

                var configEnum = module.GetType(SteamworksConfigEnumFullName);
                if (configEnum != null)
                    AddMissingRecvBufferEnumMembers(configEnum, assemblyName);

                var zdoManType = module.GetType(ZdoManTypeName);
                if (zdoManType != null)
                    RaiseSendZdosQueueCap(zdoManType, assemblyName);
            }
            catch (Exception ex)
            {
                Log.LogError($"{assemblyName}: patch failed, server will run with vanilla behavior: {ex}");
            }
        }

        private static bool IsDedicatedServer()
        {
            if (_isDedicatedServer.HasValue) return _isDedicatedServer.Value;

            try
            {
                // Mirrors FiresGhettoNetworking's ServerClientUtils.Detect() exe
                // name + exe directory checks. The other methods FGN uses
                // (Application.isBatchMode, ZNet reflection, GraphicsDeviceType)
                // aren't available at preloader-patch time — Unity isn't fully
                // initialized yet and ZNet doesn't exist — so we stick to the
                // filesystem-derived signals which work this early.
                //
                // Wildcard semantics: ".Contains(server)" on a lowercased name
                // is the equivalent of "*server*" — catches valheim_server.exe,
                // valheim_server.x86_64, valheim_server1.exe, server2.exe,
                // myserver.exe, anything an op might have renamed to.
                //
                // Directory check catches the common "Valheim Server 1\" and
                // "dedicated_server\" folder patterns where the exe itself was
                // left at vanilla name.
                string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
                string exeName = Path.GetFileNameWithoutExtension(exePath).ToLowerInvariant();
                string exeDir  = Path.GetDirectoryName(exePath)?.ToLowerInvariant() ?? string.Empty;

                if (exeName.Contains("server"))
                {
                    _isDedicatedServer = true;
                    return true;
                }

                if (exeDir.Contains("server") || exeDir.Contains("dedicated"))
                {
                    _isDedicatedServer = true;
                    return true;
                }

                _isDedicatedServer = false;
            }
            catch (Exception ex)
            {
                // Fail closed — if we can't tell what process we're in, don't
                // patch. Better to leave a server unmodified and have the user
                // notice + investigate than to silently activate on a client.
                Log.LogInfo($"Could not read process info ({ex.GetType().Name}: {ex.Message}); " +
                            "treating as 'not a server'.");
                _isDedicatedServer = false;
            }

            return _isDedicatedServer.Value;
        }

        private static bool IsGhettoNetworkingInstalled()
        {
            if (_ghettoNetworkingPresent.HasValue) return _ghettoNetworkingPresent.Value;

            try
            {
                string pluginsDir = Paths.PluginPath;
                if (string.IsNullOrEmpty(pluginsDir) || !Directory.Exists(pluginsDir))
                {
                    _ghettoNetworkingPresent = false;
                    return false;
                }

                // r2modman / Thunderstore / vortex all unpack into per-mod subfolders
                // beneath BepInEx/plugins/, so a recursive scan is mandatory.
                // Directory.GetFiles + AllDirectories is case-insensitive on the
                // NTFS / FAT filesystems Valheim ships on.
                var matches = Directory.GetFiles(
                    pluginsDir, GhettoNetworkingDllGlob, SearchOption.AllDirectories);
                _ghettoNetworkingPresent = matches.Length > 0;
            }
            catch (Exception ex)
            {
                // If we can't scan for any reason, fail closed: don't patch.
                // Logging at Info, not Error — this is a normal "no FGN" path
                // for most users and the patcher just sits idle.
                Log.LogInfo($"Could not scan plugins directory ({ex.GetType().Name}: {ex.Message}); " +
                            "treating as 'FGN not present'.");
                _ghettoNetworkingPresent = false;
            }

            return _ghettoNetworkingPresent.Value;
        }

        private static void AddMissingRecvBufferEnumMembers(TypeDefinition enumType, string assemblyName)
        {
            int added = 0;
            int alreadyPresent = 0;

            foreach (var member in RecvBufferEnumMembers)
            {
                if (EnumHasMember(enumType, member.Name))
                {
                    alreadyPresent++;
                    continue;
                }

                enumType.Fields.Add(BuildEnumLiteral(enumType, member.Name, member.Value));
                added++;
            }

            Log.LogInfo($"{assemblyName}: Steamworks enum — added {added}, {alreadyPresent} already present.");
        }

        private static FieldDefinition BuildEnumLiteral(TypeDefinition enumType, string name, int value)
        {
          const FieldAttributes EnumLiteralAttributes =
                FieldAttributes.Public |
                FieldAttributes.Static |
                FieldAttributes.Literal |
                FieldAttributes.HasDefault;

            return new FieldDefinition(name, EnumLiteralAttributes, enumType)
            {
                Constant = value,
            };
        }

        private static bool EnumHasMember(TypeDefinition enumType, string name)
        {
            foreach (var field in enumType.Fields)
            {
                if (field.Name == name) return true;
            }
            return false;
        }

        private static void RaiseSendZdosQueueCap(TypeDefinition zdoManType, string assemblyName)
        {
            var sendZdos = FindMethodByName(zdoManType, SendZdosMethodName);
            if (sendZdos == null)
            {
                Log.LogWarning($"{assemblyName}: ZDOMan.SendZDOs not found; queue cap patch skipped.");
                return;
            }

            var instructions = sendZdos.Body.Instructions;
            int raised = 0;
            int alreadyRaised = 0;

            for (int i = 0; i < instructions.Count; i++)
            {
                if (!IsQueueCapConstantAt(instructions, i, out int currentCap))
                    continue;

                if (currentCap >= ZdoSendQueueCapBytes)
                {
                    alreadyRaised++;
                    continue;
                }

                instructions[i].Operand = ZdoSendQueueCapBytes;
                raised++;
            }

            if (raised == 0 && alreadyRaised == 0)
            {
                Log.LogWarning($"{assemblyName}: no queue cap constant matched in ZDOMan.SendZDOs; vanilla behavior in effect.");
                return;
            }

            Log.LogInfo($"{assemblyName}: ZDOMan.SendZDOs queue cap — raised {raised} to {ZdoSendQueueCapBytes}, {alreadyRaised} already at target.");
        }

        private static MethodDefinition FindMethodByName(TypeDefinition type, string name)
        {
            foreach (var method in type.Methods)
            {
                if (method.Name == name) return method;
            }
            return null;
        }

        private static bool IsQueueCapConstantAt(Collection<Instruction> instructions, int index, out int value)
        {
            value = 0;
            var instruction = instructions[index];

            if (instruction.OpCode != OpCodes.Ldc_I4) return false;

            value = (int)instruction.Operand;
            if (value < VanillaZdoSendQueueCapBytes) return false;

            return IsNearGetSendQueueSizeCall(instructions, index);
        }

        private static bool IsNearGetSendQueueSizeCall(Collection<Instruction> instructions, int center)
        {
            int lo = Math.Max(0, center - QueueSizeCallSearchRadius);
            int hi = Math.Min(instructions.Count, center + QueueSizeCallSearchRadius + 1);

            for (int i = lo; i < hi; i++)
            {
                if (IsCallTo(instructions[i], GetSendQueueSizeMethodName)) return true;
            }
            return false;
        }

        private static bool IsCallTo(Instruction instruction, string methodName)
        {
            if (instruction.OpCode != OpCodes.Call && instruction.OpCode != OpCodes.Callvirt)
                return false;

            return instruction.Operand is MethodReference methodRef
                && methodRef.Name == methodName;
        }
    }
}
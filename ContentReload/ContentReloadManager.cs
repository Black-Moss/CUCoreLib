using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using BepInEx;
using BepInEx.Bootstrap;
using CUCoreLib.Helpers;
using CUCoreLib.Networking;
using Mono.Cecil;

namespace CUCoreLib.ContentReload
{
    public static class ContentReloadManager
    {
        private const string ConfigFileName = "ContentReload.json";

        private static readonly Dictionary<string, ContentReloadState> StateByModGuid =
            new Dictionary<string, ContentReloadState>(StringComparer.OrdinalIgnoreCase);

        private static bool initialized;
        private static ContentReloadConfig config;

        internal static string ConfigDirectoryPath { get; private set; }

        internal static void Initialize()
        {
            if (initialized)
            {
                return;
            }

            initialized = true;
            ConfigDirectoryPath = Path.Combine(Paths.ConfigPath, "CUCoreLib");
            config = LoadConfig();
            ContentWatchService.Initialize();
        }

        public static ContentReloadResult Reload(string modGuid)
        {
            Initialize();

            ContentReloadResult result = new ContentReloadResult
            {
                ModGuid = (modGuid ?? string.Empty).Trim()
            };

            if (string.IsNullOrWhiteSpace(modGuid))
            {
                result.AddError("Mod GUID was empty.");
                return result;
            }

            if (IsMultiplayerActive())
            {
                result.AddError("Strict content DLL reload is singleplayer-only.");
                return result;
            }

            ContentReloadState state = GetOrCreateState(modGuid);
            ContentReloadCandidate candidate = ContentAssemblyResolver.ResolveCandidate(modGuid, config, state);
            ContentCompatibilityReport report = ContentCompatibilityScanner.Scan(candidate);
            state.LastReport = report;

            result = ContentReplayExecutor.Execute(report);
            state.LastResult = result;

            if (result.Succeeded)
            {
                state.LastSuccessfulHash = result.SourceHash;
                state.LastSuccessfulSourcePath = result.SourcePath;
                state.PendingHash = null;
                state.PendingSourcePath = null;
                state.PendingSinceUtc = DateTime.MinValue;
            }

            return result;
        }

        public static string[] GetLoadedModGuids()
        {
            return Chainloader.PluginInfos.Keys
                .Where(guid => !string.Equals(guid, CUCoreLibPlugin.GUID, StringComparison.OrdinalIgnoreCase))
                .OrderBy(guid => guid, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public static int GetPollIntervalSeconds()
        {
            Initialize();
            return config != null && config.PollIntervalSeconds > 0 ? config.PollIntervalSeconds : 2;
        }

        public static bool ConfigureAutoHotRefresh(string dllPath, bool enabled, out string message)
        {
            Initialize();

            string normalizedPath = NormalizeExistingOrTargetPath(dllPath);
            if (string.IsNullOrWhiteSpace(normalizedPath))
            {
                message = "DLL path was invalid.";
                return false;
            }

            if (!File.Exists(normalizedPath))
            {
                message = "DLL path does not exist: " + normalizedPath;
                return false;
            }

            if (!TryResolvePluginGuidFromDll(normalizedPath, out string modGuid, out string modName, out string reason))
            {
                message = reason;
                return false;
            }

            if (config.Mods == null)
            {
                config.Mods = new Dictionary<string, ContentReloadModConfig>(StringComparer.OrdinalIgnoreCase);
            }

            if (!config.Mods.TryGetValue(modGuid, out ContentReloadModConfig modConfig) || modConfig == null)
            {
                modConfig = new ContentReloadModConfig();
                config.Mods[modGuid] = modConfig;
            }

            modConfig.OverrideDllPath = normalizedPath;
            modConfig.WatchEnabled = enabled;
            SaveConfig();

            string label = string.IsNullOrWhiteSpace(modName) ? modGuid : modName + " (" + modGuid + ")";
            message = (enabled ? "Enabled" : "Disabled") + " automatic hot reload for " + label + " using " + normalizedPath + ".";
            return true;
        }

        internal static void PollWatchers()
        {
            if (!initialized || IsMultiplayerActive())
            {
                return;
            }

            DateTime now = DateTime.UtcNow;
            foreach (string modGuid in GetLoadedModGuids())
            {
                if (!ContentAssemblyResolver.IsWatchEnabled(config, modGuid))
                {
                    continue;
                }

                ContentReloadState state = GetOrCreateState(modGuid);
                ContentReloadCandidate candidate = ContentAssemblyResolver.ResolveCandidate(modGuid, config, state);
                if (string.IsNullOrWhiteSpace(candidate.SelectedPath) || string.IsNullOrWhiteSpace(candidate.SelectedHash))
                {
                    continue;
                }

                if (string.Equals(candidate.SelectedHash, state.LastSuccessfulHash, StringComparison.OrdinalIgnoreCase))
                {
                    state.PendingHash = null;
                    state.PendingSourcePath = null;
                    state.PendingSinceUtc = DateTime.MinValue;
                    continue;
                }

                if (!string.Equals(state.PendingHash, candidate.SelectedHash, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(state.PendingSourcePath, candidate.SelectedPath, StringComparison.OrdinalIgnoreCase))
                {
                    state.PendingHash = candidate.SelectedHash;
                    state.PendingSourcePath = candidate.SelectedPath;
                    state.PendingSinceUtc = now;
                    continue;
                }

                int debounceMilliseconds = config != null && config.DebounceMilliseconds > 0 ? config.DebounceMilliseconds : 1200;
                if ((now - state.PendingSinceUtc).TotalMilliseconds < debounceMilliseconds)
                {
                    continue;
                }

                ContentReloadResult result = Reload(modGuid);
                if (!result.Succeeded)
                {
                    state.PendingSinceUtc = now;
                }
            }
        }

        public static void WriteReloadSummaryToConsole(ConsoleScript console, ContentReloadResult result)
        {
            string headline = BuildResultHeadline(result);
            // if (result != null && !result.Succeeded)
            // {
            //     CUCoreLibPlugin.Log?.LogWarning(headline);
            // }
            // else
            // {
            //     CUCoreLibPlugin.Log?.LogInfo(headline);
            // }
            if (console != null)
            {
                CUCoreUtils.ConsoleLog(console, headline);
            }

            WriteMessages(console, result != null ? result.RecognizedMethods.Select(method => "Recognized method: " + method).ToArray() : Array.Empty<string>());
            WriteMessages(console, result != null ? result.Info.ToArray() : Array.Empty<string>());
            WriteMessages(console, result != null ? result.Skipped.ToArray() : Array.Empty<string>());
            WriteMessages(console, result != null ? result.Errors.ToArray() : Array.Empty<string>());
            if (result != null && !string.IsNullOrWhiteSpace(result.UnsupportedReason))
            {
                WriteMessages(console, new[] { result.UnsupportedReason });
            }
        }

        private static ContentReloadState GetOrCreateState(string modGuid)
        {
            string normalizedModGuid = (modGuid ?? string.Empty).Trim();
            if (!StateByModGuid.TryGetValue(normalizedModGuid, out ContentReloadState state))
            {
                state = new ContentReloadState();
                StateByModGuid[normalizedModGuid] = state;
            }

            return state;
        }

        private static ContentReloadConfig LoadConfig()
        {
            try
            {
                Directory.CreateDirectory(ConfigDirectoryPath);
                string configPath = Path.Combine(ConfigDirectoryPath, ConfigFileName);
                if (!File.Exists(configPath))
                {
                    ContentReloadConfig created = new ContentReloadConfig();
                    File.WriteAllText(configPath, JsonConvert.SerializeObject(created, Formatting.Indented));
                    return created;
                }

                ContentReloadConfig loaded = JsonConvert.DeserializeObject<ContentReloadConfig>(File.ReadAllText(configPath));
                if (loaded == null)
                {
                    return new ContentReloadConfig();
                }

                if (loaded.Mods == null)
                {
                    loaded.Mods = new Dictionary<string, ContentReloadModConfig>(StringComparer.OrdinalIgnoreCase);
                }

                if (loaded.PollIntervalSeconds <= 0)
                {
                    loaded.PollIntervalSeconds = 2;
                }

                if (loaded.DebounceMilliseconds <= 0)
                {
                    loaded.DebounceMilliseconds = 1200;
                }

                return loaded;
            }
            catch (Exception ex)
            {
                CUCoreLibPlugin.Log?.LogWarning("Failed to load strict content reload config.\n" + ex);
                return new ContentReloadConfig();
            }
        }

        private static void SaveConfig()
        {
            try
            {
                Directory.CreateDirectory(ConfigDirectoryPath);
                string configPath = Path.Combine(ConfigDirectoryPath, ConfigFileName);
                File.WriteAllText(configPath, JsonConvert.SerializeObject(config ?? new ContentReloadConfig(), Formatting.Indented));
            }
            catch (Exception ex)
            {
                CUCoreLibPlugin.Log?.LogWarning("Failed to save strict content reload config.\n" + ex);
            }
        }

        private static bool IsMultiplayerActive()
        {
            return MultiplayerBridge.IsAvailable && MultiplayerBridge.IsRunning;
        }

        private static string NormalizeExistingOrTargetPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            try
            {
                return Path.GetFullPath(path.Trim().Trim('"'));
            }
            catch
            {
                return null;
            }
        }

        private static bool TryResolvePluginGuidFromDll(string dllPath, out string modGuid, out string modName, out string reason)
        {
            modGuid = null;
            modName = null;
            reason = null;

            try
            {
                using (AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(dllPath))
                {
                    foreach (TypeDefinition type in EnumerateTypes(assembly.MainModule.Types))
                    {
                        if (type == null || !type.HasCustomAttributes)
                        {
                            continue;
                        }

                        foreach (CustomAttribute attribute in type.CustomAttributes)
                        {
                            if (!string.Equals(attribute.AttributeType.FullName, "BepInEx.BepInPlugin", StringComparison.Ordinal))
                            {
                                continue;
                            }

                            if (attribute.ConstructorArguments.Count > 0)
                            {
                                modGuid = attribute.ConstructorArguments[0].Value as string;
                            }

                            if (attribute.ConstructorArguments.Count > 1)
                            {
                                modName = attribute.ConstructorArguments[1].Value as string;
                            }

                            if (!string.IsNullOrWhiteSpace(modGuid))
                            {
                                return true;
                            }
                        }
                    }
                }

                reason = "No [BepInPlugin] GUID was found in " + dllPath + ".";
                return false;
            }
            catch (Exception ex)
            {
                reason = "Failed to inspect DLL '" + dllPath + "': " + ex.Message;
                return false;
            }
        }

        private static IEnumerable<TypeDefinition> EnumerateTypes(IEnumerable<TypeDefinition> roots)
        {
            if (roots == null)
            {
                yield break;
            }

            foreach (TypeDefinition type in roots)
            {
                if (type == null)
                {
                    continue;
                }

                yield return type;
                foreach (TypeDefinition nested in EnumerateTypes(type.NestedTypes))
                {
                    yield return nested;
                }
            }
        }

        private static string BuildResultHeadline(ContentReloadResult result)
        {
            if (result == null)
            {
                return "Strict content reload result was null.";
            }

            string label = string.IsNullOrWhiteSpace(result.ModName) ? result.ModGuid : result.ModName + " (" + result.ModGuid + ")";
            string suffix = string.IsNullOrWhiteSpace(result.SourceHash)
                ? string.Empty
                : " hash=" + result.SourceHash + ".";
            return "Strict content reload for " + label + ": " +
                (result.Succeeded ? "success" : "failed") +
                ", " + result.Info.Count + " info, " +
                result.Skipped.Count + " skipped, " +
                result.Errors.Count + " errors." + suffix;
        }

        private static void WriteMessages(ConsoleScript console, IEnumerable<string> messages)
        {
            if (messages == null)
            {
                return;
            }

            foreach (string message in messages)
            {
                if (string.IsNullOrWhiteSpace(message))
                {
                    continue;
                }

                // CUCoreLibPlugin.Log?.LogInfo(message);
                if (console != null)
                {
                    CUCoreUtils.ConsoleLog(console, message);
                }
            }
        }
    }
}

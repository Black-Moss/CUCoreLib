using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using CUCoreLib.Data;

namespace CUCoreLib.ContentReload
{
    internal static class ContentCompatibilityScanner
    {
        private const string ContentReloadEntryAttributeFullName = "CUCoreLib.Data.ContentReloadEntryAttribute";
        private const string CclContentHostAttributeFullName = "CUCoreLib.Data.CCLContentHostAttribute";
        private const string CclReloadIgnoreAttributeFullName = "CUCoreLib.Data.CCLReloadIgnoreAttribute";

        private static readonly string[] AllowedBuildingDefinitionMembers =
        {
            "ID",
            "Name",
            "Description",
            "Sprite",
            "SpriteAnimationId",
            "SortingOrder",
            "UseGlowPlantMaterial",
            "Scale",
            "ColliderSize",
            "ColliderOffset",
            "ColliderIsTrigger",
            "Layer",
            "AddRigidbody2D",
            "RigidbodyBodyType",
            "RigidbodyGravityScale",
            "Health",
            "RequireGround",
            "Metallic",
            "CantHit",
            "Animal",
            "IgnoreBodyOptimize",
            "DropChanceMultiplier",
            "ItemsDropOnDestroy",
            "AlwaysDrop",
            "ItemCategoriesToAdd",
            "GuaranteedDropAmount",
            "Placement",
            "GenerationStyle",
            "SpawnMinPerChunk",
            "SpawnMaxPerChunk",
            "SurfaceOffset",
            "RandomFlip",
            "SpawnInGround",
            "HitSoundReferenceId",
            "HitSound",
            "BlockFootstepSoundId",
            "RenderReferenceId",
            "CopyGlowPlantLayer",
            "HeatRadius",
            "HeatPerSecond",
            "MaxHeatBodyTemperature"
        };

        internal static ContentCompatibilityReport Scan(ContentReloadCandidate candidate)
        {
            var report = new ContentCompatibilityReport
            {
                ModGuid = candidate?.ModGuid,
                ModName = candidate?.ModName,
                LoadedPluginPath = candidate?.LoadedPluginPath,
                OverridePath = candidate?.OverridePath,
                SelectedPath = candidate?.SelectedPath,
                SelectedHash = candidate?.SelectedHash,
                SelectedSourceLabel = candidate?.SelectedSourceLabel
            };

            if (candidate == null || string.IsNullOrWhiteSpace(candidate.SelectedPath))
            {
                report.UnsupportedReason =
                    "No rebuilt DLL source path was found. The loaded plugin path and runtime override path were both unavailable.";
                return report;
            }

            if (!File.Exists(candidate.SelectedPath))
            {
                report.UnsupportedReason = "The selected rebuilt DLL path does not exist: " + candidate.SelectedPath;
                return report;
            }

            try
            {
                using (var assembly =
                       AssemblyDefinition.ReadAssembly(candidate.SelectedPath, CreateReaderParameters(candidate)))
                {
                    var pluginType = FindPluginType(assembly, candidate.ModGuid);
                    if (pluginType == null)
                    {
                        report.UnsupportedReason = "No [BepInPlugin] type matching '" + candidate.ModGuid +
                                                   "' was found in the rebuilt DLL.";
                        return report;
                    }

                    report.PluginTypeFullName = pluginType.FullName;

                    var discoveryIndex = 0;
                    DiscoverExplicitPluginMethods(pluginType, report, ref discoveryIndex);
                    DiscoverContentHostMethods(assembly, report, ref discoveryIndex);
                    AddIgnoredMethodNotes(pluginType, report);

                    report.Methods.Sort((left, right) =>
                    {
                        var stage = left.Stage.CompareTo(right.Stage);
                        if (stage != 0) return stage;

                        var order = left.Order.CompareTo(right.Order);
                        if (order != 0) return order;

                        return left.DiscoveryIndex.CompareTo(right.DiscoveryIndex);
                    });

                    foreach (var discoveredMethod in report.Methods)
                        report.RecognizedMethods.Add(discoveredMethod.DisplayName);

                    if (report.Methods.Count == 0)
                    {
                        if (report.SkippedMethods.Count > 0)
                            report.UnsupportedReason =
                                "No supported content reload entry methods were found. All discovered candidates were skipped.";
                        else
                            report.UnsupportedReason =
                                "No supported content reload entry methods were found. Add one or more [ContentReloadEntry(...)] methods or mark a content class with [CCLContentHost].";
                    }
                }
            }
            catch (Exception ex)
            {
                report.UnsupportedReason = "Compatibility scan failed: " + ex.Message;
            }

            return report;
        }

        private static ReaderParameters CreateReaderParameters(ContentReloadCandidate candidate)
        {
            var resolver = new DefaultAssemblyResolver();
            foreach (var directory in BuildResolverSearchDirectories(candidate))
                try
                {
                    resolver.AddSearchDirectory(directory);
                }
                catch
                {
                    // ignored
                }

            return new ReaderParameters
            {
                AssemblyResolver = resolver,
                InMemory = true,
                ReadWrite = false,
                ReadingMode = ReadingMode.Deferred
            };
        }

        private static IEnumerable<string> BuildResolverSearchDirectories(ContentReloadCandidate candidate)
        {
            var seenDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string[] seedPaths =
            {
                candidate?.SelectedPath,
                candidate?.LoadedPluginPath,
                candidate?.OverridePath,
                typeof(CUCoreLibPlugin).Assembly.Location,
                Assembly.GetExecutingAssembly().Location
            };

            foreach (var path in seedPaths)
            {
                var directory = GetExistingDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory) && seenDirectories.Add(directory)) yield return directory;
            }

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                string directory;
                try
                {
                    directory = GetExistingDirectoryName(assembly.Location);
                }
                catch
                {
                    directory = null;
                }

                if (!string.IsNullOrWhiteSpace(directory) && seenDirectories.Add(directory)) yield return directory;
            }
        }

        private static string GetExistingDirectoryName(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return null;

            try
            {
                var fullPath = Path.GetFullPath(filePath);
                if (!File.Exists(fullPath)) return null;

                var directory = Path.GetDirectoryName(fullPath);
                return Directory.Exists(directory) ? directory : null;
            }
            catch
            {
                return null;
            }
        }

        private static void DiscoverExplicitPluginMethods(TypeDefinition pluginType, ContentCompatibilityReport report,
            ref int discoveryIndex)
        {
            if (pluginType == null) return;

            foreach (var method in pluginType.Methods)
            {
                var attribute = GetReloadEntryAttribute(method);
                if (attribute == null) continue;

                AddMethodFromAttribute(method, attribute, report, true, ref discoveryIndex);
            }
        }

        private static void DiscoverContentHostMethods(AssemblyDefinition assembly, ContentCompatibilityReport report,
            ref int discoveryIndex)
        {
            foreach (var hostType in EnumerateTypes(assembly.MainModule.Types).Where(IsContentHost))
            {
                foreach (var method in hostType.Methods)
                {
                    if (!IsEligibleHostMethod(method)) continue;

                    if (HasReloadIgnoreAttribute(method)) continue;

                    var attribute = GetReloadEntryAttribute(method);
                    if (attribute != null)
                    {
                        AddMethodFromAttribute(method, attribute, report, false, ref discoveryIndex);
                        continue;
                    }

                    InferAndAddHostMethod(method, report, ref discoveryIndex);
                }
            }
        }

        private static void AddMethodFromAttribute(MethodDefinition method, CustomAttribute attribute,
            ContentCompatibilityReport report, bool isPluginMethod, ref int discoveryIndex)
        {
            if (method.HasParameters)
            {
                var usage = isPluginMethod ? "[ContentReloadEntry]" : "[ContentReloadEntry] on content host";
                AddSkippedMethod(report, method,
                    "Method '" + BuildMethodDisplayName(method) + "' uses " + usage +
                    " but is not parameterless. Strict content reload entry methods must not take parameters.");
                return;
            }

            if (!TryReadAttributeStage(attribute, out var stageOrder))
            {
                AddSkippedMethod(report, method,
                    "Method '" + BuildMethodDisplayName(method) +
                    "' uses [ContentReloadEntry] with an unsupported stage value.");
                return;
            }

            var validationIssue = ValidateMethodForStage(method, (ContentReloadEntryStage)stageOrder, false);
            if (!string.IsNullOrWhiteSpace(validationIssue))
            {
                AddSkippedMethod(report, method, validationIssue);
                return;
            }

            AddRecognizedMethod(report, method, (ContentReloadEntryStage)stageOrder, ReadAttributeOrder(attribute),
                isPluginMethod, discoveryIndex++);
        }

        private static void InferAndAddHostMethod(MethodDefinition method, ContentCompatibilityReport report,
            ref int discoveryIndex)
        {
            if (!TryInferStage(method, out var stage, out var reason))
            {
                AddSkippedMethod(report, method, reason);
                return;
            }

            AddRecognizedMethod(report, method, stage, 0, false, discoveryIndex++);
        }

        private static bool TryInferStage(MethodDefinition method, out ContentReloadEntryStage stage, out string reason)
        {
            stage = ContentReloadEntryStage.LoadAssets;
            reason = null;

            if (method == null || !method.HasBody)
            {
                reason = "Method '" + BuildMethodDisplayName(method) +
                         "' has no method body. Strict content reload can only inspect methods with IL bodies.";
                return false;
            }

            var surfaceUsages = AnalyzeMethodSurfaceUsage(method, new HashSet<string>(StringComparer.Ordinal));

            if (!string.IsNullOrWhiteSpace(surfaceUsages.UnsupportedReason))
            {
                reason = surfaceUsages.UnsupportedReason;
                return false;
            }

            if (surfaceUsages.Surfaces.Count == 0)
            {
                if (surfaceUsages.UsesAssetLoader)
                {
                    stage = ContentReloadEntryStage.LoadAssets;
                    return true;
                }

                reason = "Method '" + BuildMethodDisplayName(method) +
                         "' did not call a supported content registration API. Add [ContentReloadEntry(...)] if you need an explicit stage.";
                return false;
            }

            if (surfaceUsages.Surfaces.Count > 1)
            {
                reason = "Method '" + BuildMethodDisplayName(method) +
                         "' touches multiple supported registration surfaces. Split the method or add explicit [ContentReloadEntry(...)] methods for each stage.";
                return false;
            }

            var surface = surfaceUsages.Surfaces.First();
            stage = SurfaceToStage(surface);
            return true;
        }

        private static SurfaceUsageAnalysis AnalyzeMethodSurfaceUsage(MethodDefinition method,
            HashSet<string> visitedMethods)
        {
            var analysis = new SurfaceUsageAnalysis();
            if (method == null || !method.HasBody) return analysis;

            var methodKey = method.FullName ?? method.Name;
            if (!visitedMethods.Add(methodKey)) return analysis;

            foreach (var instruction in method.Body.Instructions)
            {
                var calledMethod = instruction.Operand as MethodReference;
                if (calledMethod == null) continue;

                var surface = ClassifySupportedSurfaceCall(method, calledMethod, out var unsupportedReason);
                if (!string.IsNullOrWhiteSpace(unsupportedReason))
                {
                    analysis.UnsupportedReason = unsupportedReason;
                    return analysis;
                }

                if (surface.HasValue) analysis.Surfaces.Add(surface.Value);

                if (IsAssetLoaderCall(calledMethod)) analysis.UsesAssetLoader = true;

                MethodDefinition nestedMethod;
                try
                {
                    nestedMethod = calledMethod.Resolve();
                }
                catch
                {
                    nestedMethod = null;
                }

                if (nestedMethod == null || nestedMethod.Module != method.Module) continue;

                var nestedAnalysis = AnalyzeMethodSurfaceUsage(nestedMethod, visitedMethods);
                if (!string.IsNullOrWhiteSpace(nestedAnalysis.UnsupportedReason))
                {
                    analysis.UnsupportedReason = nestedAnalysis.UnsupportedReason;
                    return analysis;
                }

                analysis.UsesAssetLoader |= nestedAnalysis.UsesAssetLoader;
                foreach (var nestedSurface in nestedAnalysis.Surfaces) analysis.Surfaces.Add(nestedSurface);
            }

            return analysis;
        }

        private static ContentReloadEntryStage SurfaceToStage(ContentReloadSurface surface)
        {
            switch (surface)
            {
                case ContentReloadSurface.Locale:
                    return ContentReloadEntryStage.RegisterLocale;
                case ContentReloadSurface.Liquids:
                    return ContentReloadEntryStage.RegisterLiquids;
                case ContentReloadSurface.Items:
                    return ContentReloadEntryStage.RegisterItems;
                case ContentReloadSurface.Buildings:
                    return ContentReloadEntryStage.RegisterBuildings;
                case ContentReloadSurface.Recipes:
                    return ContentReloadEntryStage.RegisterRecipes;
                default:
                    return ContentReloadEntryStage.LoadAssets;
            }
        }

        private static string ValidateMethodForStage(MethodDefinition method, ContentReloadEntryStage stage,
            bool allowSurfaceMismatch)
        {
            if (stage == ContentReloadEntryStage.RegisterBuildings)
            {
                var buildingIssue = FindBuildingDefinitionIssue(method, new HashSet<string>(StringComparer.Ordinal));
                if (!string.IsNullOrWhiteSpace(buildingIssue)) return buildingIssue;
            }

            var analysis = AnalyzeMethodSurfaceUsage(method, new HashSet<string>(StringComparer.Ordinal));
            if (!string.IsNullOrWhiteSpace(analysis.UnsupportedReason)) return analysis.UnsupportedReason;

            if (allowSurfaceMismatch || analysis.Surfaces.Count == 0) return null;

            if (analysis.Surfaces.Count > 1)
                return "Method '" + BuildMethodDisplayName(method) +
                       "' touches multiple supported registration surfaces. Split the method or add explicit [ContentReloadEntry(...)] methods for each stage.";

            var requiredSurface = StageToSurface(stage);
            if (requiredSurface == ContentReloadSurface.None) return null;

            return analysis.Surfaces.Contains(requiredSurface)
                ? null
                : "Method '" + BuildMethodDisplayName(method) + "' is tagged for stage '" + stage +
                  "' but does not call the matching supported registration API.";
        }

        private static ContentReloadSurface StageToSurface(ContentReloadEntryStage stage)
        {
            switch (stage)
            {
                case ContentReloadEntryStage.RegisterText:
                case ContentReloadEntryStage.RegisterLocale:
                    return ContentReloadSurface.Locale;
                case ContentReloadEntryStage.RegisterLiquids:
                    return ContentReloadSurface.Liquids;
                case ContentReloadEntryStage.RegisterItems:
                    return ContentReloadSurface.Items;
                case ContentReloadEntryStage.RegisterBuildings:
                    return ContentReloadSurface.Buildings;
                case ContentReloadEntryStage.RegisterRecipes:
                    return ContentReloadSurface.Recipes;
                default:
                    return ContentReloadSurface.None;
            }
        }

        private static ContentReloadSurface? ClassifySupportedSurfaceCall(MethodDefinition callingMethod,
            MethodReference calledMethod, out string unsupportedReason)
        {
            unsupportedReason = null;

            var declaringType = calledMethod.DeclaringType != null
                ? calledMethod.DeclaringType.FullName
                : string.Empty;
            var methodName = calledMethod.Name ?? string.Empty;

            if (string.Equals(declaringType, "CUCoreLib.Registries.ItemRegistry", StringComparison.Ordinal) &&
                string.Equals(methodName, "Register", StringComparison.Ordinal))
                return ContentReloadSurface.Items;

            if (string.Equals(declaringType, "CUCoreLib.Registries.LiquidRegistry", StringComparison.Ordinal) &&
                string.Equals(methodName, "Register", StringComparison.Ordinal))
                return ContentReloadSurface.Liquids;

            if (string.Equals(declaringType, "CUCoreLib.Registries.RecipeRegistry", StringComparison.Ordinal) &&
                string.Equals(methodName, "Register", StringComparison.Ordinal))
                return ContentReloadSurface.Recipes;

            if (string.Equals(declaringType, "CUCoreLib.Registries.LocaleRegistry", StringComparison.Ordinal) &&
                methodName.StartsWith("Register", StringComparison.Ordinal))
                return ContentReloadSurface.Locale;

            if (string.Equals(declaringType, "CUCoreLib.Helpers.LocaleLoader", StringComparison.Ordinal))
                return ContentReloadSurface.Locale;

            if (string.Equals(declaringType, "CUCoreLib.Registries.BuildingEntityRegistry",
                    StringComparison.Ordinal))
            {
                if (string.Equals(methodName, "AddDrop", StringComparison.Ordinal)) return null;

                if (string.Equals(methodName, "Register", StringComparison.Ordinal))
                {
                    var buildingDefinitionIssue = FindUnsupportedBuildingDefinitionUsage(callingMethod, calledMethod);
                    if (!string.IsNullOrWhiteSpace(buildingDefinitionIssue))
                    {
                        unsupportedReason = buildingDefinitionIssue;
                        return null;
                    }

                    return ContentReloadSurface.Buildings;
                }

                unsupportedReason = "Method '" + BuildMethodDisplayName(callingMethod) +
                                    "' calls BuildingEntityRegistry." + methodName +
                                    "(). Only basic building registration is supported during strict content reload.";
                return null;
            }

            if (string.Equals(declaringType, "CUCoreLib.Registries.ModOptionsRegistry", StringComparison.Ordinal))
            {
                unsupportedReason = BuildUnsupportedReason(callingMethod, "it calls ModOptionsRegistry." + methodName +
                                                                         "(). Mod options are excluded from strict content reload.");
                return null;
            }

            if (string.Equals(declaringType, "CUCoreLib.Registries.SaveRegistry", StringComparison.Ordinal))
            {
                unsupportedReason = BuildUnsupportedReason(callingMethod, "it calls SaveRegistry." + methodName +
                                                                         "(). Save providers are excluded from strict content reload.");
                return null;
            }

            if (string.Equals(declaringType, "CUCoreLib.Registries.MoodleRegistry", StringComparison.Ordinal) ||
                string.Equals(declaringType, "CUCoreLib.Registries.StatusRegistry", StringComparison.Ordinal))
            {
                unsupportedReason = BuildUnsupportedReason(callingMethod,
                    "it calls " + calledMethod.DeclaringType?.Name + "." + methodName +
                    "(). Status and moodle registration are excluded from strict content reload.");
                return null;
            }

            if (string.Equals(declaringType, "CUCoreLib.Networking.MultiplayerApi", StringComparison.Ordinal) ||
                string.Equals(declaringType, "CUCoreLib.Networking.MultiplayerBridge", StringComparison.Ordinal) ||
                string.Equals(declaringType, "CUCoreLib.Networking.MultiplayerSyncRegistry",
                    StringComparison.Ordinal))
            {
                unsupportedReason = BuildUnsupportedReason(callingMethod,
                    "it calls multiplayer registration/setup code. Multiplayer hooks are excluded from strict content reload.");
                return null;
            }

            if (string.Equals(declaringType, "CUCoreLib.Registries.TileRegistry", StringComparison.Ordinal))
            {
                unsupportedReason = BuildUnsupportedReason(callingMethod, "it calls TileRegistry." + methodName +
                                                                         "(). Tile registration is excluded from strict content reload.");
                return null;
            }

            if (string.Equals(declaringType, "CUCoreLib.Registries.StructureRegistry", StringComparison.Ordinal))
            {
                unsupportedReason = BuildUnsupportedReason(callingMethod, "it calls StructureRegistry." + methodName +
                                                                         "(). Structure registration is excluded from strict content reload.");
                return null;
            }

            if (string.Equals(declaringType, "CUCoreLib.Registries.ConsoleCommandRegistry",
                    StringComparison.Ordinal))
            {
                unsupportedReason = BuildUnsupportedReason(callingMethod,
                    "it calls ConsoleCommandRegistry." + methodName +
                    "(). Console command registration is excluded from strict content reload.");
                return null;
            }

            if (string.Equals(declaringType, "HarmonyLib.Harmony", StringComparison.Ordinal) ||
                string.Equals(declaringType, "HarmonyLib.HarmonyMethod", StringComparison.Ordinal))
            {
                unsupportedReason = BuildUnsupportedReason(callingMethod,
                    "it performs Harmony setup. Patch registration is excluded from strict content reload.");
                return null;
            }

            if (string.Equals(declaringType, "CUCoreLib.Helpers.CustomInstantiate", StringComparison.Ordinal) ||
                string.Equals(declaringType, "Body", StringComparison.Ordinal) &&
                (string.Equals(methodName, "PickUpItem", StringComparison.Ordinal) ||
                 string.Equals(methodName, "DropItem", StringComparison.Ordinal)))
            {
                unsupportedReason = BuildUnsupportedReason(callingMethod,
                    "it mutates the live scene or inventory. Runtime scene side effects are excluded from strict content reload.");
                return null;
            }

            return null;
        }

        private static string BuildUnsupportedReason(MethodDefinition method, string detail)
        {
            return "Method '" + BuildMethodDisplayName(method) + "' is not strict content-only: " + detail;
        }

        private static string FindBuildingDefinitionIssue(MethodDefinition method, HashSet<string> visitedMethods)
        {
            if (method == null || !method.HasBody) return null;

            var methodKey = method.FullName ?? method.Name;
            if (!visitedMethods.Add(methodKey)) return null;

            foreach (var instruction in method.Body.Instructions)
            {
                var calledMethod = instruction.Operand as MethodReference;
                if (calledMethod == null) continue;

                if (string.Equals(calledMethod.DeclaringType?.FullName, "CUCoreLib.Registries.BuildingEntityRegistry",
                        StringComparison.Ordinal) &&
                    string.Equals(calledMethod.Name, "Register", StringComparison.Ordinal))
                {
                    var issue = FindUnsupportedBuildingDefinitionUsage(method, calledMethod);
                    if (!string.IsNullOrWhiteSpace(issue)) return issue;
                }

                MethodDefinition nestedMethod;
                try
                {
                    nestedMethod = calledMethod.Resolve();
                }
                catch
                {
                    nestedMethod = null;
                }

                if (nestedMethod == null || nestedMethod.Module != method.Module) continue;

                var nestedIssue = FindBuildingDefinitionIssue(nestedMethod, visitedMethods);
                if (!string.IsNullOrWhiteSpace(nestedIssue)) return nestedIssue;
            }

            return null;
        }

        private static bool IsAssetLoaderCall(MethodReference calledMethod)
        {
            return string.Equals(calledMethod.DeclaringType?.FullName, "CUCoreLib.Helpers.AssetLoader",
                       StringComparison.Ordinal) ||
                   string.Equals(calledMethod.DeclaringType?.FullName, "CUCoreLib.Helpers.FileLoader",
                       StringComparison.Ordinal);
        }

        private static CustomAttribute GetReloadEntryAttribute(MethodDefinition method)
        {
            return method?.CustomAttributes.FirstOrDefault(entry =>
                string.Equals(entry.AttributeType.FullName, ContentReloadEntryAttributeFullName,
                    StringComparison.Ordinal));
        }

        private static bool HasReloadIgnoreAttribute(MethodDefinition method)
        {
            return method != null && method.CustomAttributes.Any(entry =>
                string.Equals(entry.AttributeType.FullName, CclReloadIgnoreAttributeFullName, StringComparison.Ordinal));
        }

        private static bool IsContentHost(TypeDefinition type)
        {
            return type != null && type.CustomAttributes.Any(entry =>
                string.Equals(entry.AttributeType.FullName, CclContentHostAttributeFullName, StringComparison.Ordinal));
        }

        private static bool IsEligibleHostMethod(MethodDefinition method)
        {
            if (method == null) return false;
            if (method.IsConstructor || method.IsGetter || method.IsSetter || method.IsAddOn || method.IsRemoveOn)
                return false;
            if (method.HasParameters) return false;
            if (method.ReturnType != null && method.ReturnType.FullName != "System.Void") return false;
            if ((method.Attributes & Mono.Cecil.MethodAttributes.SpecialName) != 0) return false;
            if (method.Name.StartsWith("<", StringComparison.Ordinal)) return false;

            return true;
        }

        private static bool TryReadAttributeStage(CustomAttribute attribute, out int stageOrder)
        {
            stageOrder = -1;
            if (attribute == null || attribute.ConstructorArguments.Count < 1) return false;

            var rawValue = attribute.ConstructorArguments[0].Value;
            if (rawValue == null) return false;

            try
            {
                stageOrder = Convert.ToInt32(rawValue);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static int ReadAttributeOrder(CustomAttribute attribute)
        {
            if (attribute == null) return 0;

            foreach (var property in attribute.Properties)
            {
                if (!string.Equals(property.Name, "Order", StringComparison.Ordinal)) continue;

                try
                {
                    return Convert.ToInt32(property.Argument.Value);
                }
                catch
                {
                    return 0;
                }
            }

            return 0;
        }

        private static TypeDefinition FindPluginType(AssemblyDefinition assembly, string modGuid)
        {
            if (assembly == null) return null;

            foreach (var type in EnumerateTypes(assembly.MainModule.Types))
            {
                if (!type.HasCustomAttributes) continue;

                foreach (var attribute in type.CustomAttributes)
                {
                    if (!string.Equals(attribute.AttributeType.FullName, "BepInEx.BepInPlugin",
                            StringComparison.Ordinal)) continue;

                    if (attribute.ConstructorArguments.Count > 0 &&
                        string.Equals(attribute.ConstructorArguments[0].Value as string, modGuid,
                            StringComparison.Ordinal))
                        return type;
                }
            }

            return null;
        }

        private static IEnumerable<TypeDefinition> EnumerateTypes(IEnumerable<TypeDefinition> roots)
        {
            if (roots == null) yield break;

            foreach (var type in roots)
            {
                if (type == null) continue;

                yield return type;
                foreach (var nested in EnumerateTypes(type.NestedTypes)) yield return nested;
            }
        }

        private static void AddIgnoredMethodNotes(TypeDefinition pluginType, ContentCompatibilityReport report)
        {
            string[] ignoredMethodNames =
            {
                "RegisterBuildings",
                "RegisterBuildingEntities",
                "RegisterStatuses",
                "RegisterMoodles",
                "RegisterOptions",
                "RegisterSettings",
                "RegisterTiles",
                "RegisterSave",
                "RegisterSaveProviders"
            };

            foreach (var ignoredMethodName in ignoredMethodNames)
                if (pluginType.Methods.Any(method =>
                        string.Equals(method.Name, ignoredMethodName, StringComparison.Ordinal)))
                    report.Notes.Add("Ignored unsupported method '" + ignoredMethodName +
                                     "' during strict content scan.");
        }

        private static void AddRecognizedMethod(ContentCompatibilityReport report, MethodDefinition method,
            ContentReloadEntryStage stage, int order, bool isPluginMethod, int discoveryIndex)
        {
            var displayName = BuildMethodDisplayName(method);
            if (report.Methods.Any(existing => string.Equals(existing.DisplayName, displayName, StringComparison.Ordinal)))
                return;

            report.Methods.Add(new DiscoveredReloadMethod
            {
                DisplayName = displayName,
                DeclaringTypeFullName = method.DeclaringType.FullName,
                MethodName = method.Name,
                IsStatic = method.IsStatic,
                IsPluginMethod = isPluginMethod,
                Stage = stage,
                Order = order,
                DiscoveryIndex = discoveryIndex
            });
        }

        private static void AddSkippedMethod(ContentCompatibilityReport report, MethodDefinition method, string reason)
        {
            var displayName = BuildMethodDisplayName(method);
            if (report.SkippedMethods.Any(existing =>
                    string.Equals(existing.DisplayName, displayName, StringComparison.Ordinal) &&
                    string.Equals(existing.Reason, reason, StringComparison.Ordinal)))
                return;

            report.SkippedMethods.Add(new SkippedReloadMethod
            {
                DisplayName = displayName,
                Reason = reason
            });
        }

        private static string BuildMethodDisplayName(MethodDefinition method)
        {
            if (method == null) return "<unknown method>";

            var declaringType = method.DeclaringType != null ? method.DeclaringType.FullName : "<unknown type>";
            return declaringType + "." + method.Name;
        }

        private static string FindUnsupportedBuildingDefinitionUsage(MethodDefinition callingMethod,
            MethodReference calledMethod)
        {
            if (callingMethod == null || !callingMethod.HasBody || calledMethod == null) return null;

            IList<Instruction> instructions = callingMethod.Body.Instructions;
            for (var i = 0; i < instructions.Count; i++)
            {
                if (!ReferenceEquals(instructions[i].Operand, calledMethod)) continue;

                for (var scanIndex = i - 1; scanIndex >= 0; scanIndex--)
                {
                    var scan = instructions[scanIndex];
                    var ctorReference = scan.Operand as MethodReference;
                    if (scan.OpCode != OpCodes.Newobj || ctorReference == null) continue;

                    var ctorDeclaringType = ctorReference.DeclaringType;
                    if (ctorDeclaringType == null ||
                        !string.Equals(ctorDeclaringType.FullName, "CUCoreLib.Data.CustomBuildingEntityDefinition",
                            StringComparison.Ordinal))
                        break;

                    return ValidateBuildingDefinitionInitialization(instructions, scanIndex, i);
                }

                return null;
            }

            return null;
        }

        private static string ValidateBuildingDefinitionInitialization(IList<Instruction> instructions, int startIndex,
            int endIndex)
        {
            var allowedMembers = new HashSet<string>(AllowedBuildingDefinitionMembers, StringComparer.Ordinal);
            for (var i = startIndex + 1; i < endIndex; i++)
            {
                var instruction = instructions[i];
                var member = instruction.Operand as MemberReference;
                var declaringType = member?.DeclaringType;
                if (member == null ||
                    declaringType == null ||
                    !string.Equals(declaringType.FullName, "CUCoreLib.Data.CustomBuildingEntityDefinition",
                        StringComparison.Ordinal))
                    continue;

                var memberName = member.Name ?? string.Empty;
                if (string.Equals(memberName, "ConfigurePrefab", StringComparison.Ordinal) ||
                    string.Equals(memberName, "ConfigureInstance", StringComparison.Ordinal) ||
                    string.Equals(memberName, "PlaceCheck", StringComparison.Ordinal) ||
                    string.Equals(memberName, "Components", StringComparison.Ordinal) ||
                    string.Equals(memberName, "SpawnComponents", StringComparison.Ordinal) ||
                    instruction.OpCode == OpCodes.Stfld && !allowedMembers.Contains(memberName))
                    return "Method '" + member.DeclaringType.FullName +
                           "' registers a building definition using unsupported member '" + memberName +
                           "'. Only basic/scriptless building definitions can be hot reloaded.";
            }

            return null;
        }

        private sealed class SurfaceUsageAnalysis
        {
            public HashSet<ContentReloadSurface> Surfaces { get; } = new HashSet<ContentReloadSurface>();
            public bool UsesAssetLoader { get; set; }
            public string UnsupportedReason { get; set; }
        }
    }
}

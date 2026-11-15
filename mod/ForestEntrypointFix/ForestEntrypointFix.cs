using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using Mono.Cecil;

/// <summary>
/// The Forest ships two Unity builds in one folder, sharing one BepInEx install and config: the flat-screen game
/// (Unity 5.6, everything in UnityEngine.dll) and the VR game (Unity 2017.4, split into UnityEngine.CoreModule.dll).
/// BepInEx hooks its chainloader into "[Preloader.Entrypoint] Assembly" from BepInEx.cfg. That value is only defaulted
/// for the build that starts first and then saved, so the other build looks for an assembly it doesn't have and never
/// loads any plugin (silently: the plugin log is only opened by the chainloader).
///
/// Preloader patchers are initialized before BepInEx reads that setting, so this sets it for the running build.
/// </summary>
public static class ForestEntrypointFix
{
    // Required by BepInEx for a patcher; nothing is patched here.
    public static IEnumerable<string> TargetDLLs { get { yield break; } }

    public static void Patch(AssemblyDefinition assembly) { }

    public static void Initialize()
    {
        var log = Logger.CreateLogSource("ForestEntrypointFix");
        try
        {
            var wanted = File.Exists(Path.Combine(Paths.ManagedPath, "UnityEngine.CoreModule.dll"))
                ? "UnityEngine.CoreModule.dll"
                : "UnityEngine.dll";
            var preloader = Type.GetType("BepInEx.Preloader.Preloader, BepInEx.Preloader");
            var field = preloader?.GetField("ConfigEntrypointAssembly", BindingFlags.NonPublic | BindingFlags.Static);
            if (!(field?.GetValue(null) is ConfigEntry<string> entry))
            {
                log.LogWarning("BepInEx's entrypoint setting not found; is this BepInEx 5.4?");
                return;
            }
            if (entry.Value == wanted) return;
            log.LogInfo($"Entrypoint assembly {entry.Value} -> {wanted} for {Paths.ProcessName}");
            entry.Value = wanted;
        }
        catch (Exception e)
        {
            log.LogError(e);
        }
        finally
        {
            Logger.Sources.Remove(log);
        }
    }
}

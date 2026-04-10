using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;

namespace Common.Hooks;

public enum HookType
{
    Hook,
    ILHook,
}

public static class HookGen
{
    static readonly Assembly executingAssembly = Assembly.GetExecutingAssembly();
    static readonly FieldInfo fieldOwnedHookLists = typeof(MonoMod.RuntimeDetour.HookGen.HookEndpointManager)
        .GetField("OwnedHookLists", BindingFlags.NonPublic | BindingFlags.Static);

    public static void UnpatchSelf()
        => MonoMod.RuntimeDetour.HookGen.HookEndpointManager.RemoveAllOwnedBy(executingAssembly);

    /// <returns>
    /// Item1 (HookType) can be 0 (Hook), or 1 (ILHook).
    /// </returns>
    public static IEnumerable<(HookType, MethodBase, Delegate)> GetPatchedMethods()
    {
        var dict = (IDictionary)fieldOwnedHookLists.GetValue(null);
        if (!dict.Contains(executingAssembly))
            return [];

        var hookEntries = (IEnumerable<object>)dict[executingAssembly];
        var first = hookEntries.FirstOrDefault();
        if (first is null)
            return [];

        var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        var t = first.GetType();

        var typeField = t.GetField("Type", flags);
        var methodField = t.GetField("Method", flags);
        var hookField = t.GetField("Hook", flags);

        return hookEntries.Select(hookEntry =>
        (
            (HookType)typeField.GetValue(hookEntry),
            (MethodBase)methodField.GetValue(hookEntry),
            (Delegate)hookField.GetValue(hookEntry)
        ));
    }
}

public class ManagedHooks(ManualLogSource logger) : IDisposable
{
    readonly ManualLogSource logger = logger;
    readonly Dictionary<Delegate, List<IDetour>> detourLists = [];

    public void Dispose()
    {
        foreach (var detours in detourLists.Values)
            detours.ForEach(detour => detour.Dispose());
        detourLists.Clear();
    }

    public void Add(MethodBase method, Delegate to)
    {
        if (detourLists.TryGetValue(to, out var detours))
        {
            var isDuplicate = detours
                .OfType<Hook>()
                .Any(hook => hook.Method.Equals(method) && hook.Target.Equals(to.Method));
            if (!isDuplicate)
                detours.Append(new Hook(method, to));
            else
                logger.LogDebug(
                    $"{nameof(ManagedHooks)}: Skipping duplicate Hook: {GetHookString(method, to.Method)}");
        }
        else
            detourLists[to] = [new Hook(method, to)];
    }

    public void Add(MethodBase method, ILContext.Manipulator manipulator)
    {
        if (detourLists.TryGetValue(manipulator, out var detours))
        {
            var isDuplicate = detours
                .OfType<ILHook>()
                .Any(hook => hook.Method.Equals(method));
            if (!isDuplicate)
                detours.Append(new ILHook(method, manipulator));
            else
                logger.LogDebug(
                    $"{nameof(ManagedHooks)}: Skipping duplicate ILHook: "
                    + GetILHookString(method, manipulator.Method));
        }
        else
            detourLists[manipulator] = [new ILHook(method, manipulator)];
    }

    public IEnumerable<(HookType, MethodBase, MethodBase, bool)> GetAllPatchedMethods()
        => GetAllHookedMethods()
            .Select(it => (HookType.Hook, it.Item1, it.Item2, it.Item3))
            .Concat(
                GetAllModifiedMethods()
                .Select(it => (HookType.ILHook, it.Item1, it.Item2, it.Item3)));

    public IEnumerable<(MethodBase, MethodBase, bool)> GetAllHookedMethods()
        => detourLists.Values
            .SelectMany(it => it)
            .OfType<Hook>()
            .Select(hook => (hook.Method, hook.Target, hook.IsApplied));

    public IEnumerable<(MethodBase, MethodBase, bool)> GetAllModifiedMethods()
        => detourLists
            .SelectMany(
                kvp => kvp.Value.OfType<ILHook>(),
                (kvp, hook) => (kvp.Key, hook))
            .Select(tuple =>
            {
                var (manipulator, hook) = tuple;
                return (hook.Method, (MethodBase)manipulator.Method, hook.IsApplied);
            });

    public void ApplyDelegate(Delegate hookDelegate)
    {
        if (detourLists.TryGetValue(hookDelegate, out var detours))
            detours.ForEach(detour => detour.Apply());
    }

    public void UndoDelegate(Delegate hookDelegate)
    {
        if (detourLists.TryGetValue(hookDelegate, out var detours))
            detours.ForEach(detour => detour.Undo());
    }

    public void DisposeDelegate(Delegate hookDelegate)
    {
        if (detourLists.TryGetValue(hookDelegate, out var detours))
        {
            detours.ForEach(detour => detour.Dispose());
            detourLists.Remove(hookDelegate);
        }
    }

    /// <summary>
    /// Does nothing if `Debug` log level is unset.
    /// </summary>
    public void LogPatchedMethods(bool includeHookGen = false)
    {
        static string GetMsg(HookType kind, MethodBase from, MethodBase to)
        {
            return kind switch
            {
                HookType.Hook => $"Hooked: {GetHookString(from, to)}",
                HookType.ILHook => $"IL Patched: {GetILHookString(from, to)}",
                _ => ""
            };
        }

        if (!BepInEx.Logging.LogLevel.HasFlag(LogLevel.Debug))
            return;

        foreach (var (kind, method, target, _) in GetAllPatchedMethods().Where(it => it.Item4))
        {
            var msg = GetMsg(kind, method, target);
            logger.LogDebug(msg);
        }

        if (includeHookGen)
        {
            foreach (var (kind, method, target) in HookGen.GetPatchedMethods())
            {
                var msg = GetMsg(kind, method, target.Method);
                logger.LogDebug(msg);
            }
        }
    }

    static string GetHookString(MethodBase from, MethodBase to)
        => $"{from.DeclaringType.Name}.{from.Name} -> {to.DeclaringType.Name}.{to.Name}";

    static string GetILHookString(MethodBase from, MethodBase to)
        => $"{from.DeclaringType.Name}.{from.Name}: Manipulator={to.DeclaringType.Name}.{to.Name}";
}
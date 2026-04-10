using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;

namespace Common.Hooks;

public static class Utils
{
    static readonly Assembly executingAssembly = Assembly.GetExecutingAssembly();

    public static void HookGenUnpatchSelf()
        => MonoMod.RuntimeDetour.HookGen.HookEndpointManager.RemoveAllOwnedBy(executingAssembly);
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
                    $"{nameof(ManagedHooks)}: Skipping duplicate ILHook: {GetHookString(method, manipulator.Method)}");
        }
        else
            detourLists[manipulator] = [new ILHook(method, manipulator)];
    }

    public IEnumerable<(MethodBase, MethodBase, bool)> GetAllPatchedMethods()
        => GetAllHookedMethods().Concat(GetAllModifiedMethods());

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

    public void LogAllPatchedMethods()
    {
        foreach (var (method, target, isActive) in GetAllPatchedMethods())
        {
            var msg = $"Patched({isActive}): {GetHookString(method, target)}";
            if (isActive)
                logger.LogInfo(msg);
            else
                logger.LogError(msg);
        }
    }

    string GetHookString(MethodBase from, MethodBase to)
        => $"{from.DeclaringType.Name}.{from.Name} -> {to.DeclaringType.Name}.{to.Name}";
}
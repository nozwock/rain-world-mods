using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using MonoMod.RuntimeDetour;

namespace Common.Hooks;

public class ManagedHooks(ManualLogSource logger) : IDisposable
{
    readonly ManualLogSource logger = logger;
    readonly Dictionary<Delegate, List<Hook>> hookLists = [];

    public void Dispose()
    {
        foreach (var hooks in hookLists.Values)
            hooks.ForEach(hook => hook.Dispose());
        hookLists.Clear();
    }

    public void Add(MethodBase method, Delegate to)
    {
        if (hookLists.TryGetValue(to, out var hooks))
        {
            var isDuplicate = hooks
                .Any(hook => hook.Method == method && hook.Target == to.Method);
            if (!isDuplicate)
                hooks.Append(new(method, to));
            else
                logger.LogDebug(
                    $"{nameof(ManagedHooks)}: Skipping duplicate hook: {GetHookString(method, to.Method)}");
        }
        else
            hookLists[to] = [new(method, to)];
    }

    public (MethodBase, MethodBase, bool)[] GetAllHookedMethods()
        => [.. hookLists.Values.SelectMany(it => it)
            .Select(hook => (hook.Method, hook.Target, hook.IsApplied))];

    public void ApplyDelegate(Delegate hookDelegate)
    {
        if (hookLists.TryGetValue(hookDelegate, out var hooks))
            hooks.ForEach(hook => hook.Apply());
    }

    public void UndoDelegate(Delegate hookDelegate)
    {
        if (hookLists.TryGetValue(hookDelegate, out var hooks))
            hooks.ForEach(hook => hook.Undo());
    }

    public void DisposeDelegate(Delegate hookDelegate)
    {
        if (hookLists.TryGetValue(hookDelegate, out var hooks))
        {
            hooks.ForEach(hook => hook.Dispose());
            hookLists.Remove(hookDelegate);
        }
    }

    public void LogAllHookedMethods()
    {
        foreach (var (method, target, isActive) in GetAllHookedMethods())
        {
            var msg = $"Hooked({isActive}): {GetHookString(method, target)}";
            if (isActive)
                logger.LogInfo(msg);
            else
                logger.LogError(msg);
        }
    }

    string GetHookString(MethodBase from, MethodBase to)
        => $"{from.DeclaringType.Name}.{from.Name} -> {to.DeclaringType.Name}.{to.Name}";
}
using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Logging;
using MonoMod.RuntimeDetour;
using MoreSlugcats;

namespace InfinitePassages;

[BepInAutoPlugin(id: "nozwock.InfinitePassages")]
public partial class Plugin : BaseUnityPlugin
{
    internal static new ManualLogSource Logger;

    bool isInit;

    List<Hook> customHooks;

    // TODO: Remix config support

    public void OnEnable()
    {
        if (isInit)
            return;
        isInit = true;

        Logger = base.Logger;

        Logger.LogInfo($"Plugin {Id} is loaded!");

        On.RainWorldGame.CustomEndGameSaveAndRestart += Hook_RainWorldGame_CustomEndGameSaveAndRestart;
        On.Menu.EndgameTokens.ctor += Hook_EndgameTokens_ctor;
        On.WinState.ConsumeEndGame += Hook_WinState_ConsumeEndGame;
        On.WinState.GetNextEndGame += Hook_WinState_GetNextEndGame;

        customHooks =
        [
            // Prevent earnedPassages decrement in Menu.SleepAndDeathScreen.Singal
            new(
                typeof(Expedition.ExpeditionData)
                .GetProperty(nameof(Expedition.ExpeditionData.earnedPassages))
                .GetSetMethod(),
                (Action<Action<int>, int>)((orig, value) =>
                {
                    if (value < Expedition.ExpeditionData.earnedPassages)
                        return;
                    orig.Invoke(value);
                })),
        ];
        LogCustomHooks(customHooks);
    }

    public void OnDisable()
    {
        if (!isInit)
            return;
        isInit = false;

        Logger.LogInfo($"Unloading plugin {Id}");

        On.RainWorldGame.CustomEndGameSaveAndRestart -= Hook_RainWorldGame_CustomEndGameSaveAndRestart;
        On.Menu.EndgameTokens.ctor -= Hook_EndgameTokens_ctor;
        On.WinState.ConsumeEndGame -= Hook_WinState_ConsumeEndGame;
        On.WinState.GetNextEndGame -= Hook_WinState_GetNextEndGame;

        foreach (var hook in customHooks)
        {
            hook?.Dispose();
        }
    }

    void LogCustomHooks(List<Hook> hooks)
    {
        foreach (var hook in hooks)
        {
            var msg = $"Custom Hook: {hook.Method.DeclaringType?.Name}.{hook.Method.Name}: "
                + $"valid={hook.IsValid}, active={hook.IsApplied}";
            if (hook.IsApplied && hook.IsValid)
            {
                Logger.LogInfo(msg);
            }
            else
            {
                Logger.LogError(msg);
            }
        }
    }

    void Hook_RainWorldGame_CustomEndGameSaveAndRestart(
        On.RainWorldGame.orig_CustomEndGameSaveAndRestart orig,
        RainWorldGame self,
        bool addFiveCycles)
    {
        orig.Invoke(self, false);
    }

    // Reset consumed state for all tokens, for when the tokens are already consumed before the mod is applied
    void Hook_EndgameTokens_ctor(
        On.Menu.EndgameTokens.orig_ctor orig,
        Menu.EndgameTokens self,
        Menu.Menu menu,
        Menu.MenuObject owner,
        UnityEngine.Vector2 pos,
        FContainer container,
        Menu.KarmaLadder ladder)
    {
        ladder.endGameMeters.ForEach(it => it.tracker.consumed = false);

        orig.Invoke(self, menu, owner, pos, container, ladder);
    }

    // Skip
    void Hook_WinState_ConsumeEndGame(On.WinState.orig_ConsumeEndGame orig, WinState self) { }

    // GetNextEndGame: Which "passage" to use for fast travel
    WinState.EndgameID Hook_WinState_GetNextEndGame(On.WinState.orig_GetNextEndGame orig, WinState self)
    {
        if (self.endgameTrackers.Count < 1)
        {
            return orig.Invoke(self);
        }

        // TODO: Optionally skip passage's image on clicking "Passage" for fast travel (Menu.SleepAndDeathScreen.Singal)
        // SleepAndDeathScreen.Singal
        //      endGameSceneCounter = 1
        //          switch to CustomEndGameScreen in SleepAndDeathScreen.Update()
        //
        // Replace switch to CustomEndGameScreen with:
        // manager.RequestMainProcessSwitch(ProcessManager.ProcessID.FastTravelScreen);


        // Randomize the passage being used to avoid always using the same passage (cosmetic) since passages are never
        // consumed
        var availablePassageIds = self.endgameTrackers
            .Where(it => it.GoalFullfilled
                && (!ModManager.MSC || it.ID != MoreSlugcatsEnums.EndgameID.Gourmand))
            .Select(it => it.ID);

        return availablePassageIds.ElementAt(new Random().Next(0, availablePassageIds.Count()));
    }
}

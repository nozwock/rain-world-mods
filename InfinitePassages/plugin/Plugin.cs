using System;
using System.Linq;
using System.Reflection;
using System.Security.Permissions;
using BepInEx;
using BepInEx.Logging;
using Common.Hooks;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MoreSlugcats;

// Allows access to private members on runtime
#pragma warning disable CS0618 // It's enforced by the mono runtime game's using
[assembly: SecurityPermission(SecurityAction.RequestMinimum, SkipVerification = true)]
#pragma warning restore CS0618

namespace InfinitePassages;

[BepInAutoPlugin(id: "nozwock.InfinitePassages")]
public partial class Plugin : BaseUnityPlugin
{
    internal static new ManualLogSource Logger;

    bool isInit;

    ManagedHooks managedHooks;

    Configurable<bool> configSkipPassageAnimation;
    Configurable<bool> configNoKarmaRecovery;

    public void OnEnable()
    {
        Logger = base.Logger;

        On.RainWorld.OnModsInit += Hook_RainWorld_OnModsInit;
    }

    void Hook_RainWorld_OnModsInit(On.RainWorld.orig_OnModsInit orig, RainWorld self)
    {
        orig.Invoke(self);

        if (isInit)
            return;
        isInit = true;

        Logger.LogInfo($"Plugin {Id} is loaded!");

        try
        {
            // https://rainworldmodding.miraheze.org/wiki/Creating_a_Remix_Menu#Method_1:_Automatically_generating_a_remix_menu
            var optionInterface = MachineConnector.GetRegisteredOI(Id);
            var config = optionInterface.config;

            configSkipPassageAnimation = config.Bind(
                // Will silently fail if key contains spaces
                "SkipPassageAnimation",
                defaultValue: true,
                new ConfigurableInfo(
                    "Jump straight to Fast Travel screen instead of opening the Passage Token image screen.",
                    autoTab: "General")
                );
            configSkipPassageAnimation.OnChange += InitHooks_SkipPassageAnimation;

            configNoKarmaRecovery = config.Bind(
                "NoKarmaRecovery",
                defaultValue: true,
                new ConfigurableInfo(
                    "Don't regain Max Karma on Fast Travel.",
                    autoTab: "General")
                );
            configNoKarmaRecovery.OnChange += InitHooks_NoKarmaRecovery;

            InitHooks();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex);
        }
    }

    void InitHooks()
    {
        managedHooks = new(Logger);

        On.RainWorldGame.CustomEndGameSaveAndRestart += Hook_RainWorldGame_CustomEndGameSaveAndRestart;
        On.Menu.EndgameTokens.ctor += Hook_EndgameTokens_ctor;
        On.WinState.ConsumeEndGame += Hook_WinState_ConsumeEndGame;
        On.WinState.GetNextEndGame += Hook_WinState_GetNextEndGame;

        InitHooks_SkipPassageAnimation();
        InitHooks_NoKarmaRecovery();

        managedHooks.Add(
            typeof(Expedition.ExpeditionData)
            .GetProperty(nameof(Expedition.ExpeditionData.earnedPassages))
            .GetSetMethod(),
            Hook_ExpeditionData_set_earnedPassages);
    }

    public void OnDisable()
    {
        if (!isInit)
            return;
        isInit = false;

        Logger.LogInfo($"Unloading plugin {Id}");

        try
        {
            MonoMod.RuntimeDetour.HookGen.HookEndpointManager
                .RemoveAllOwnedBy(Assembly.GetExecutingAssembly());

            managedHooks.Dispose();

            configSkipPassageAnimation.OnChange -= InitHooks_SkipPassageAnimation;
            configNoKarmaRecovery.OnChange -= InitHooks_NoKarmaRecovery;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex);
        }
    }

    void InitHooks_SkipPassageAnimation()
    {
        On.Menu.SleepAndDeathScreen.Singal -= Hook_SleepAndDeathScreen_Singal;
        IL.Menu.SleepAndDeathScreen.Update -= IL_SleepAndDeathScreen_Update;
        if (configSkipPassageAnimation.Value)
        {
            On.Menu.SleepAndDeathScreen.Singal += Hook_SleepAndDeathScreen_Singal;
            IL.Menu.SleepAndDeathScreen.Update += IL_SleepAndDeathScreen_Update;
        }
    }

    void InitHooks_NoKarmaRecovery()
    {
        IL.SaveState.ApplyCustomEndGame -= IL_SaveState_ApplyCustomEndGame;
        if (configNoKarmaRecovery.Value)
        {
            IL.SaveState.ApplyCustomEndGame += IL_SaveState_ApplyCustomEndGame;
        }
    }

    // Prevent Expedition gamemode's earnedPassages decrement in Menu.SleepAndDeathScreen.Singal
    void Hook_ExpeditionData_set_earnedPassages(Action<int> orig, int value)
    {
        if (value < Expedition.ExpeditionData.earnedPassages)
            return;
        orig(value);
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

    // Skip setting passage .consumed = true
    void Hook_WinState_ConsumeEndGame(On.WinState.orig_ConsumeEndGame orig, WinState self) { }

    // Which "passage" to use for fast travel
    WinState.EndgameID Hook_WinState_GetNextEndGame(On.WinState.orig_GetNextEndGame orig, WinState self)
    {
        if (self.endgameTrackers.Count < 1)
        {
            return orig.Invoke(self);
        }

        // Randomize the passage being used to avoid always using the same passage (cosmetic) since passages are never
        // consumed
        var availablePassageIds = self.endgameTrackers
            .Where(it => it.GoalFullfilled
                && (!ModManager.MSC || it.ID != MoreSlugcatsEnums.EndgameID.Gourmand))
            .Select(it => it.ID);

        return availablePassageIds.ElementAt(new Random().Next(0, availablePassageIds.Count()));
    }

    void Hook_SleepAndDeathScreen_Singal(
        On.Menu.SleepAndDeathScreen.orig_Singal orig,
        Menu.SleepAndDeathScreen self,
        Menu.MenuObject sender,
        string message)
    {
        orig.Invoke(self, sender, message);

        // Skip Passage Token icon glowing animation on clicking "Passage"
        self.endGameSceneCounter = 999999;
    }

    // Skip Passage Token image screen on clicking "Passage" for fast travel
    void IL_SleepAndDeathScreen_Update(ILContext il)
    {
        var cursor = new ILCursor(il);

        var name = nameof(ProcessManager.ProcessID.CustomEndGameScreen);
        cursor.GotoNext(MoveType.Before,
            i => i.MatchLdsfld<ProcessManager.ProcessID>(name)
                || i.MatchLdfld<ProcessManager.ProcessID>(name),
            i => i.MatchCallOrCallvirt<ProcessManager>(nameof(ProcessManager.RequestMainProcessSwitch)));

        var field = typeof(ProcessManager.ProcessID)
            .GetField(nameof(ProcessManager.ProcessID.FastTravelScreen));
        cursor.Next.OpCode = OpCodes.Ldsfld;
        cursor.Next.Operand = il.Import(field);
    }

    void IL_SaveState_ApplyCustomEndGame(ILContext il)
    {
        var cursor = new ILCursor(il);

        cursor.GotoNext(
            MoveType.Before,
            i => i.MatchLdfld<DeathPersistentSaveData>(nameof(DeathPersistentSaveData.karmaCap)),
            i => i.MatchStfld<DeathPersistentSaveData>(nameof(DeathPersistentSaveData.karma))
        );
        cursor.Next.Operand = typeof(DeathPersistentSaveData)
            .GetField(nameof(DeathPersistentSaveData.karma));

        // There's WorldCoordinate? karmaFlowerPosition as well that is getting nulled, but it's probably best to
        // let the flower be reset assuming the player is moving far away from the region the flower is in

        // cursor.Index = 0;
        // cursor.GotoNext(
        //     MoveType.Before,
        //     i => i.MatchLdarg(0),
        //     i => i.MatchLdfld<SaveState>(nameof(SaveState.deathPersistentSaveData)),
        //     i => i.MatchLdflda<DeathPersistentSaveData>(nameof(DeathPersistentSaveData.karmaFlowerPosition)),
        //     i => i.MatchInitobj<WorldCoordinate?>()
        // );
        // cursor.RemoveRange(4);
    }
}

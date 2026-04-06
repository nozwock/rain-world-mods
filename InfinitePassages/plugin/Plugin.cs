using System;
using System.Linq;
using BepInEx;
using BepInEx.Logging;
using MoreSlugcats;

namespace InfinitePassages;

[BepInAutoPlugin(id: "nozwock.InfinitePassages")]
public partial class Plugin : BaseUnityPlugin
{
	internal static new ManualLogSource Logger;

	bool isInit;

	// TODO: Remix config support
	// TODO: Support Expedition mode passages as well (Menu.SleepAndDeathScreen.Singal)

	public void OnEnable()
	{
		if (isInit)
			return;
		isInit = true;

		Logger = base.Logger;

		Logger.LogInfo($"Plugin {Id} is loaded!");

		On.WinState.ConsumeEndGame += Hook_WinState_ConsumeEndGame;
		On.WinState.GetNextEndGame += Hook_WinState_GetNextEndGame;
	}

	public void OnDisable()
	{
		if (!isInit)
			return;
		isInit = false;

		Logger.LogInfo($"Unloading plugin {Id}");

		On.WinState.ConsumeEndGame -= Hook_WinState_ConsumeEndGame;
		On.WinState.GetNextEndGame -= Hook_WinState_GetNextEndGame;
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

		// Reset consumed state for all passages (cosmetic)
		// TODO: Make this optional
		self.endgameTrackers.ForEach(it => it.consumed = false);

		// TODO: Optionally skip passage's image on clicking "Passage" for fast travel (Menu.SleepAndDeathScreen.Singal)

		// Randomize the passage being used to avoid always using the same passage (cosmetic) since passages are never
		// consumed
		var availablePassageIds = self.endgameTrackers
			.Where(it => it.GoalFullfilled
				&& (!ModManager.MSC || it.ID != MoreSlugcatsEnums.EndgameID.Gourmand))
			.Select(it => it.ID);

		return availablePassageIds.ElementAt(new Random().Next(0, availablePassageIds.Count()));
	}
}

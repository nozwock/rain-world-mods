using System;
using BepInEx;
using Common.Hooks;
using RWCustom;

namespace BetterMap;

[BepInAutoPlugin(id: "nozwock.BetterMap")]
public partial class Plugin : BaseUnityPlugin
{
    bool isInit;

    ManagedHooks managedHooks;

    public Plugin()
    {
        managedHooks = new(Logger);
    }

    public void OnEnable() => On.RainWorld.OnModsInit += Hook_RainWorld_OnModsInit;

    void Hook_RainWorld_OnModsInit(On.RainWorld.orig_OnModsInit orig, RainWorld self)
    {
        orig(self);

        if (isInit)
            return;
        isInit = true;

        Logger.LogInfo($"Plugin {Id} is loaded!");

        try
        {
            var optionInterface = MachineConnector.GetRegisteredOI(Id);
            var config = optionInterface.config;

            InitHooks();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex);
        }
    }

    public void OnDisable()
    {
        if (!isInit)
            return;
        isInit = false;

        Logger.LogInfo($"Unloading plugin {Id}");

        try
        {
            HookGen.UnpatchSelf();
            managedHooks.Dispose();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex);
        }
    }

    void InitHooks()
    {
        On.HUD.Map.DiscoverMap += Hook_Map_DiscoverMap;

        managedHooks.LogPatchedMethods(includeHookGen: true);
    }

    // TODO: Do the uncovering of rooms (or parts of the rooms) in MoveCamera instead of DiscoverMap (Map.Update)
    // On.RoomCamera.MoveCamera_int
    // On.RoomCamera.MoveCamera_Room_int

    void Hook_Map_DiscoverMap(On.HUD.Map.orig_DiscoverMap orig, HUD.Map self, IntVector2 texturePos)
    {
        // There's room.aidataprepro.aiMap.getAITile(x, y).visibility from AIdataPreprocessor.VisibilityMapper that was
        // tried before GetVisibleRect was found but it seems to be vision cone for the creature AI

        if (self.hud.owner is Player player
            && player.room != null)
        {
            var (start, end) = GetVisibleRoomArea(self, player.room, 0);

            if (IsNotDiscovered(self, start.x, start.y)
                || IsNotDiscovered(self, start.x, end.y)
                || IsNotDiscovered(self, end.x, end.y)
                || IsNotDiscovered(self, end.x, start.y)
                // XXX Would need some flag to mark section as discovered if not running in Map.Update
                || IsNotDiscovered(
                    self,
                    UnityEngine.Random.Range(start.x, end.x),
                    UnityEngine.Random.Range(start.y, end.y)))
            {
                for (var x = start.x; x < end.x; x++)
                {
                    for (var y = start.y; y < end.y; y++)
                    {
                        self.discoverTexture.SetPixel(x, y, new(1f, 1f, 1f));
                    }
                }
            }
        }

        orig(self, texturePos);
    }

    static (IntVector2, IntVector2) GetVisibleRoomArea(HUD.Map map, Room room, int margin)
    {
        var rect = room.game.cameras[0].GetVisibleRect(margin, widescreen: false);
        var start = IntVector2.FromVector2(
            map.OnTexturePos(
                new(rect.xMin, rect.yMin),
                room.abstractRoom.index,
                accountForLayer: true)
            / map.DiscoverResolution);
        var end = IntVector2.FromVector2(
            map.OnTexturePos(
                new(rect.xMax, rect.yMax),
                room.abstractRoom.index,
                accountForLayer: true)
            / map.DiscoverResolution);
        return (start, end);
    }

    static bool IsNotDiscovered(HUD.Map map, int x, int y) => map.discoverTexture.GetPixel(x, y).r == 0f;
}

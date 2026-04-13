using System;
using System.Collections.Generic;
using BepInEx;
using Common.Hooks;
using Common.RainWorld;
using Newtonsoft.Json;
using RWCustom;

namespace BetterMap;

public class ProgressionData
{
    /// <summary>
    /// bool is true if room corresponding to RoomName is fully uncovered, meant for full room uncover mode.
    /// </summary>
    public Dictionary<(string RoomName, int CamPos), bool> DiscoveredMapAreas { get; set; } = [];
}

[BepInAutoPlugin(id: "nozwock.BetterMap")]
public partial class Plugin : BaseUnityPlugin
{
    static readonly UnityEngine.Color discoveredTextureColor = new(1f, 1f, 1f);

    bool isInit;
    ManagedHooks managedHooks;
    ProgressionData progressionData = new();

    Configurable<bool> cfgInstantMapReveal = new(true);

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

            cfgInstantMapReveal = config.Bind(
                "InstantMapRevealDiscovered",
                cfgInstantMapReveal.Value,
                new ConfigurableInfo("Reveal all of the discovered map instantly once the map is opened"));

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
        SaveGame.Init();

        On.HUD.Map.ctor += Hook_Map_ctor;

        On.RoomCamera.MoveCamera_int += Hook_RoomCamera_MoveCamera_int;
        On.RoomCamera.MoveCamera_Room_int += Hook_RoomCamera_MoveCamera_Room_int;

        SaveGame.ProgressionData.RegisterRead(Id, ProgressionData_Read);
        SaveGame.ProgressionData.RegisterWrite(Id, ProgressionData_Write);

        managedHooks.LogPatchedMethods(includeHookGen: true);
    }

    void ProgressionData_Read(string obj)
    {
        if (JsonConvert.DeserializeObject<ProgressionData>(obj) is { } data)
            progressionData = data;
    }

    string ProgressionData_Write() => JsonConvert.SerializeObject(progressionData);

    void Hook_RoomCamera_MoveCamera_int(
        On.RoomCamera.orig_MoveCamera_int orig,
        RoomCamera self,
        int camPos)
    {
        orig(self, camPos);
        UncoverVisibleRoomArea(self, camPos);
    }

    void Hook_Map_ctor(
        On.HUD.Map.orig_ctor orig,
        HUD.Map self,
        HUD.HUD hud,
        HUD.Map.MapData mapData)
    {
        orig(self, hud, mapData);
        if (!self.revealAllDiscovered) // Game revealAllDiscovered sets for fast travel/region map
            self.revealAllDiscovered = cfgInstantMapReveal.Value;
    }

    void Hook_RoomCamera_MoveCamera_Room_int(
        On.RoomCamera.orig_MoveCamera_Room_int orig,
        RoomCamera self,
        Room newRoom,
        int camPos)
    {
        orig(self, newRoom, camPos);
        UncoverVisibleRoomArea(self, camPos);
    }

    void UncoverVisibleRoomArea(RoomCamera camera, int camPos)
    {
        var map = camera.hud?.map;
        if (map is null)
            return;

        // Sanity checks borrowed from near Map.DiscoverMap()
        if (map.mapLoaded
            && map.discLoaded
            && map.hud.owner.MapDiscoveryActive
            && map.discoverTexture is { } discoverTexture
            && map.hud.owner is Player player
            && player.abstractCreature.Room?.realizedRoom is { } room
            && !progressionData.DiscoveredMapAreas.ContainsKey((room.abstractRoom.name, camPos)))
        {
            var (start, end) = GetVisibleRoomArea(map, room, 0);

            for (var x = start.x; x <= end.x; x++)
            {
                for (var y = start.y; y <= end.y; y++)
                {
                    discoverTexture.SetPixel(x, y, discoveredTextureColor);
                }
            }

            progressionData.DiscoveredMapAreas.Add((room.abstractRoom.name, camPos), false);
        }
    }

    static (IntVector2, IntVector2) GetVisibleRoomArea(HUD.Map map, Room room, int margin)
    {
        // There's room.aidataprepro.aiMap.getAITile(x, y).visibility from AIdataPreprocessor.VisibilityMapper that was
        // tried before GetVisibleRect was found but it seems to be vision cone for the creature AI
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

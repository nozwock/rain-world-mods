using System;
using System.Collections.Generic;
using BepInEx;
using Common.Hooks;
using Common.RainWorld;
using Newtonsoft.Json;
using RWCustom;

namespace MapRemix;

public class ProgressionData
{
    // Newtonsoft doesn't seem to support complex types as dictionary keys by default
    public record struct MapAreaId(string RoomName, int CamPos)
    {
        // https://github.com/JamesNK/Newtonsoft.Json/issues/516#issuecomment-1325112839
        public static explicit operator MapAreaId(string value)
        {
            var splits = value.Split(['|'], 2);
            if (splits.Length == 2 && int.TryParse(splits[0], out var camPos))
                return new(splits[1], camPos);

            throw new ArgumentException($"Invalid {nameof(MapAreaId)}: {value}");
        }

        public override readonly string ToString() => $"{CamPos}|{RoomName}";
    }

    /// <summary>
    /// bool is true if room corresponding to RoomName is fully uncovered, meant for full room uncover mode.
    /// </summary>
    public Dictionary<MapAreaId, bool> DiscoveredMapAreas { get; set; } = [];
}

[BepInAutoPlugin(id: "nozwock.MapRemix")]
public partial class Plugin : BaseUnityPlugin
{
    static readonly UnityEngine.Color discoveredTextureColor = new(1f, 1f, 1f);

    bool isInit;
    ManagedHooks managedHooks;
    ProgressionData progressionData = new();

    static RemixConfig config = new(null);

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
            if (!config.IsInit)
            {
                config = new();
                MachineConnector.SetRegisteredOI(Id, config);
            }

            config.OnResetDiscoveredMapCache = () =>
            {
                Logger.LogDebug($"Clearing DiscoveredMapAreas: {progressionData.DiscoveredMapAreas.Count}");
                progressionData.DiscoveredMapAreas.Clear();
            };
            config.cfgMapDiscoveryMode.OnChange += OnChange_MapDiscoveryMode;
            config.cfgInstantMapReveal.OnChange += OnChange_InstantMapReveal;
            config.cfgInstantDiscoveredAreaReveal.OnChange += OnChange_InstantDiscoveredAreaReveal;

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
            SaveGame.Reset();

            HookGen.UnpatchSelf();
            managedHooks.Dispose();

            config.cfgMapDiscoveryMode.OnChange -= OnChange_MapDiscoveryMode;
            config.cfgInstantMapReveal.OnChange -= OnChange_InstantMapReveal;
            config.cfgInstantDiscoveredAreaReveal.OnChange -= OnChange_InstantDiscoveredAreaReveal;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex);
        }
    }

    void InitHooks()
    {
        SaveGame.Init();

        OnChange_MapDiscoveryMode();
        OnChange_InstantMapReveal();
        OnChange_InstantDiscoveredAreaReveal();

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

    void OnChange_InstantDiscoveredAreaReveal()
    {
        On.HUD.Map.ctor -= Hook_Map_ctor;
        On.HUD.Map.InitiateMapView -= Hook_Map_InitiateMapView;
        if (config.cfgInstantDiscoveredAreaReveal.Value)
        {
            On.HUD.Map.ctor += Hook_Map_ctor;
            On.HUD.Map.InitiateMapView += Hook_Map_InitiateMapView;
        }
    }

    void Hook_Map_ctor(
        On.HUD.Map.orig_ctor orig,
        HUD.Map self,
        HUD.HUD hud,
        HUD.Map.MapData mapData)
    {
        orig(self, hud, mapData);

        // Game also sets revealAllDiscovered for fast travel/region map
        self.revealAllDiscovered = true;
    }

    void Hook_Map_InitiateMapView(On.HUD.Map.orig_InitiateMapView orig, HUD.Map self)
    {
        self.resetRevealCounter = 0;
        orig(self);
    }

    void OnChange_InstantMapReveal()
    {
        On.HUD.Map.Update -= Hook_Map_Update;
        if (config.cfgInstantMapReveal.Value)
        {
            On.HUD.Map.Update += Hook_Map_Update;
        }
    }

    void Hook_Map_Update(On.HUD.Map.orig_Update orig, HUD.Map self)
    {
        self.fadeCounter = 999999;
        orig(self);
    }

    void OnChange_MapDiscoveryMode()
    {
        On.RoomCamera.MoveCamera_int -= Hook_RoomCamera_MoveCamera_int;
        On.RoomCamera.MoveCamera_Room_int -= Hook_RoomCamera_MoveCamera_Room_int;
        switch (config.cfgMapDiscoveryMode.Value)
        {
            case MapDiscoveryMode.Vanilla:
                break;
            case MapDiscoveryMode.VisibleRoomArea or MapDiscoveryMode.WholeRoom:
                On.RoomCamera.MoveCamera_int += Hook_RoomCamera_MoveCamera_int;
                On.RoomCamera.MoveCamera_Room_int += Hook_RoomCamera_MoveCamera_Room_int;
                break;
        }
    }

    void Hook_RoomCamera_MoveCamera_int(
        On.RoomCamera.orig_MoveCamera_int orig,
        RoomCamera self,
        int camPos)
    {
        orig(self, camPos);
        try
        {
            UncoverRoom(self, camPos);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex);
        }
    }

    void Hook_RoomCamera_MoveCamera_Room_int(
        On.RoomCamera.orig_MoveCamera_Room_int orig,
        RoomCamera self,
        Room newRoom,
        int camPos)
    {
        orig(self, newRoom, camPos);
        try
        {
            UncoverRoom(self, camPos);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex);
        }
    }

    // Original Player.MapDiscoveryActive always returns false within MoveCamera due to player.room which always seem to
    // came out null
    bool MapDiscoveryActive(HUD.Map map)
    {
        if (map.hud.owner is not Player player)
            return map.hud.owner.MapDiscoveryActive;

        if (
            player.Consious
            && player.AI == null
            && player.abstractCreature.Room?.realizedRoom is { } room
            && !room.world.singleRoomWorld
            && player.dangerGrasp == null
            && player.mainBodyChunk.pos.x > 0f
            && player.mainBodyChunk.pos.x < room.PixelWidth
            && player.mainBodyChunk.pos.y > 0f)
        {
            return player.mainBodyChunk.pos.y < room.PixelHeight;
        }

        return false;
    }

    void UncoverRoom(RoomCamera camera, int camPos)
    {
        var map = camera.hud?.map;
        if (map is null)
            return;

        // Sanity checks borrowed from near Map.DiscoverMap()
        if (map.mapLoaded
            && map.discLoaded
            && MapDiscoveryActive(map)
            && map.discoverTexture is { } discoverTexture
            && map.hud.owner is Player player
            && player.abstractCreature.Room?.realizedRoom is { } room)
        {
            var mapAreaId = new ProgressionData.MapAreaId(room.abstractRoom.name, camPos);
            var discoveryMode = config.cfgMapDiscoveryMode.Value;

            (IntVector2 Start, IntVector2 End) rect;
            switch (discoveryMode)
            {
                case MapDiscoveryMode.VisibleRoomArea:
                    if (progressionData.DiscoveredMapAreas.ContainsKey(mapAreaId))
                        return;

                    rect = GetVisibleRoomArea(map, room, 0);
                    if (config.cfgWorkaroundInaccurateVisibleAreaBound.Value)
                    {
                        // What we get from GetVisibleRoomArea (map scale) actually covers an area that's slightly
                        // bigger than the screen size.
                        // Like avg 12x5 when it should be 8x5 (non-widescreen) instead
                        //
                        // Hardcoded: 8x5 is what screen seems to be on map scale
                        NormalizeDifference(ref rect.Start.x, ref rect.End.x, targetDiff: 8);
                        NormalizeDifference(ref rect.Start.y, ref rect.End.y, targetDiff: 5);
                    }

                    break;
                case MapDiscoveryMode.WholeRoom:
                    if (progressionData.DiscoveredMapAreas.TryGetValue(mapAreaId, out var roomUncovered)
                        && roomUncovered)
                        return;
                    rect = GetRoomArea(map, room);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(discoveryMode));
            }

            var (start, end) = rect;
            for (var x = start.x; x <= end.x; x++)
            {
                for (var y = start.y; y <= end.y; y++)
                {
                    discoverTexture.SetPixel(x, y, discoveredTextureColor);
                }
            }

            if (progressionData.DiscoveredMapAreas.ContainsKey(mapAreaId))
            {
                if (discoveryMode == MapDiscoveryMode.WholeRoom)
                    progressionData.DiscoveredMapAreas[mapAreaId] = true;
            }
            else
            {
                progressionData.DiscoveredMapAreas.Add(mapAreaId, discoveryMode == MapDiscoveryMode.WholeRoom);
            }
        }
    }

    static (IntVector2 Start, IntVector2 End) GetRoomArea(HUD.Map map, Room room)
    {
        // https://github.com/SchuhBaum/MapOptions/blob/4a798511f82bcde75206e3f4a6c9351465d819ea/SourceCode/MapMod.cs#L370
        var start = IntVector2.FromVector2(
            map.OnTexturePos(
                new(),
                room.abstractRoom.index,
                accountForLayer: true)
            / map.DiscoverResolution);
        var end = IntVector2.FromVector2(
            map.OnTexturePos(
                room.abstractRoom.size.ToVector2() * 20,
                room.abstractRoom.index,
                accountForLayer: true)
            / map.DiscoverResolution);
        return (start, end);
    }

    static (IntVector2 Start, IntVector2 End) GetVisibleRoomArea(HUD.Map map, Room room, int margin)
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

    static void NormalizeDifference(ref int start, ref int end, int targetDiff)
    {
        int center = (start + end) / 2;

        start = center - targetDiff / 2;
        end = center + targetDiff / 2;
    }

    static bool IsNotDiscovered(HUD.Map map, int x, int y) => map.discoverTexture.GetPixel(x, y).r == 0f;
}

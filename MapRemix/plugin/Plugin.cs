using System;
using System.Collections.Generic;
using BepInEx;
using Common.Hooks;
using Common.RainWorld;
using Newtonsoft.Json;
using RWCustom;
using UnityEngine;

namespace MapRemix;

public class ProgressionData
{
    [JsonConverter(typeof(RoomAreaIdConverter))]
    public record struct RoomAreaId(string RoomName, int CamPos)
    {
        // To save space, use our custom to/from string when type is not used as dictionary key, instead of the default
        // object converter
        class RoomAreaIdConverter : JsonConverter<RoomAreaId>
        {
            public override void WriteJson(JsonWriter writer, RoomAreaId value, JsonSerializer serializer)
                => writer.WriteValue(value.ToString());

            public override RoomAreaId ReadJson(
                JsonReader reader,
                Type objectType,
                RoomAreaId existingValue,
                bool hasExistingValue,
                JsonSerializer serializer)
            {
                if (reader.TokenType == JsonToken.String)
                    return (RoomAreaId)(string)reader.Value!;
                throw new JsonSerializationException($"Unexpected token {reader.TokenType}");
            }
        }

        // Newtonsoft doesn't seem to support complex types as dictionary keys by default
        // https://github.com/JamesNK/Newtonsoft.Json/issues/516#issuecomment-1325112839
        public static explicit operator RoomAreaId(string value)
        {
            var splits = value.Split(['|'], 2);
            if (splits.Length == 2 && int.TryParse(splits[0], out var camPos))
                return new(splits[1], camPos);

            throw new ArgumentException($"Invalid {nameof(RoomAreaId)}: {value}");
        }

        public override readonly string ToString() => $"{CamPos}|{RoomName}";
    }

    public HashSet<RoomAreaId> DiscoveredRoomAreas { get; set; } = [];
    public HashSet<string> DiscoveredRooms { get; set; } = [];
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
                Logger.LogDebug($"Clearing cache: areas={progressionData.DiscoveredRoomAreas.Count}, "
                + $"rooms={progressionData.DiscoveredRooms.Count}");
                progressionData.DiscoveredRoomAreas.Clear();
                progressionData.DiscoveredRooms.Clear();
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

        // Removing room bounds check as it sometimes give false when we do want Uncover room to proceed
        if (
            player.Consious
            && player.AI == null
            && player.abstractCreature.Room?.realizedRoom is { } room
            && !room.world.singleRoomWorld)
        {
            return player.dangerGrasp == null;
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
            var mapAreaId = new ProgressionData.RoomAreaId(room.abstractRoom.name, camPos);
            if (progressionData.DiscoveredRooms.Contains(mapAreaId.RoomName))
                return;

            var discoveryMode = config.cfgMapDiscoveryMode.Value;

            (IntVector2 Start, IntVector2 End) rect;
            switch (discoveryMode)
            {
                case MapDiscoveryMode.VisibleRoomArea:
                    if (progressionData.DiscoveredRoomAreas.Contains(mapAreaId))
                        return;

                    rect = GetMapRoomAreaRect(map, camera, room, camPos);
                    break;
                case MapDiscoveryMode.WholeRoom:
                    rect = GetMapRoomRect(map, room);
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

            if (discoveryMode == MapDiscoveryMode.WholeRoom)
                progressionData.DiscoveredRooms.Add(mapAreaId.RoomName);
            else if (discoveryMode == MapDiscoveryMode.VisibleRoomArea)
                progressionData.DiscoveredRoomAreas.Add(mapAreaId);
        }
    }

    static (IntVector2 Start, IntVector2 End) GetMapRoomRect(HUD.Map map, Room room)
        => (GetMapTexturePos(new(room.RoomRect.left, room.RoomRect.bottom), map, room),
                GetMapTexturePos(new(room.RoomRect.right, room.RoomRect.top), map, room));

    static (IntVector2 Start, IntVector2 End) GetMapRoomAreaRect(
        HUD.Map map,
        RoomCamera camera,
        Room room,
        int camPos)
    {
        var (Start, End) = GetRoomAreaRect(camera, room, camPos);
        return (GetMapTexturePos(Start, map, room), GetMapTexturePos(End, map, room));
    }

    static IntVector2 GetMapTexturePos(Vector2 pos, HUD.Map map, Room room)
        => IntVector2.FromVector2(
            map.OnTexturePos(pos, room.abstractRoom.index, accountForLayer: true) / map.DiscoverResolution);

    static (Vector2 Start, Vector2 End) GetRoomAreaRect(RoomCamera camera, Room room, int camPos)
    {
        // RoomCamera.GetVisibleRect just has some weird assumptions and gives wrong bounds
        var pos = room.cameraPositions[camPos];
        pos.x += camera.hDisplace + 8;
        pos.y += 18;
        return (new(pos.x, pos.y), new(pos.x + camera.sSize.x, pos.y + camera.sSize.y));
    }

    static bool IsNotDiscovered(HUD.Map map, int x, int y) => map.discoverTexture.GetPixel(x, y).r == 0f;
}

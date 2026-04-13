using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Common.RainWorld;

// https://rainworldmodding.miraheze.org/wiki/User:Randi_Moth/Save_Data
static class SaveGame
{
    public abstract class SaveGameBase
    {
        // bool is the preprocess flag, for base64 encoding/decoding of the value in key-value save string
        // It serves as escaping for game's custom save format's special meaning literals like `<dpA>`, and others
        protected Dictionary<Action<string>, Dictionary<string, bool>> _readers = [];
        protected Dictionary<Func<string>, Dictionary<string, bool>> _writers = [];

        public abstract string ParentDelimiter { get; }
        public abstract string FieldDelimiter { get; }
        public abstract List<string>? UnrecognizedSaveStrings { get; }

        static string GetMethodFullName(MethodInfo method)
        {
            var type = method.DeclaringType;
            return $"{type.Assembly.GetName().Name}::{type.FullName}.{method.Name}";
        }

        string GetSaveString(string key, string value) => $"{key}{FieldDelimiter}{value}";

        /// <summary>
        /// Only works once game has started, i.e. `Singleton.Game` is not null
        /// </summary>
        public string? Read(string key, bool preprocess = true)
        {
            var unrecognizedSaveStrings = UnrecognizedSaveStrings;
            if (unrecognizedSaveStrings is null)
                return null;

            foreach (var saveString in unrecognizedSaveStrings)
            {
                var splits = Regex.Split(saveString, FieldDelimiter);
                if (splits.Length < 2)
                    continue;

                var sKey = splits[0];
                if (key == sKey)
                {
                    var value = splits[1];
                    if (preprocess)
                    {
                        // Let the exceptions flow
                        var decodedValue = Encoding.UTF8.GetString(Convert.FromBase64String(value));
                        return decodedValue;
                    }
                    else
                    {
                        return value;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Only works once game has started, i.e. `Singleton.Game` is not null
        /// </summary>
        public bool Write(string key, string value, bool preprocess = true)
        {
            var unrecognizedSaveStrings = UnrecognizedSaveStrings;
            if (unrecognizedSaveStrings is null)
                return false;

            Remove(key);
            if (preprocess)
            {
                var encodedValue = Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
                unrecognizedSaveStrings.Add(GetSaveString(key, encodedValue));
            }
            else
            {
                unrecognizedSaveStrings.Add(GetSaveString(key, value));
            }

            return true;
        }

        /// <summary>
        /// Only works once game has started, i.e. `Singleton.Game` is not null
        /// </summary>
        public bool Remove(string key, int count = 1)
        {
            var unrecognizedSaveStrings = UnrecognizedSaveStrings;
            if (unrecognizedSaveStrings is null)
                return false;

            var removals = 0;
            var removeCount = unrecognizedSaveStrings.RemoveAll(saveString =>
            {
                if (count > 0 && removals >= count)
                    return false;

                var splits = Regex.Split(saveString, FieldDelimiter);
                if (splits.Length < 1)
                    return false;

                var sKey = splits[0];
                var isRemove = key == sKey;
                if (isRemove)
                    removals++;

                return isRemove;
            });

            return removeCount > 0;
        }

        public void RegisterRead(string key, Action<string> reader, bool preprocess = true)
            => RegisterDelegate(_readers, key, reader, preprocess);

        /// <exception cref="ArgumentException"></exception>
        public void RegisterWrite(string key, Func<string> writer, bool preprocess = true)
        {
            if (_writers.SelectMany(kvp => kvp.Value.Select(kvp2 => kvp2.Key)).Contains(key))
            {
                // Since there's no order for registered writers, we can't even have the logic of letting the last
                // writer take precedence.
                // Support for this could be added later if desired by maintaining a separate (key -> writer[]) just for
                // ordering's sake.
                throw new ArgumentException(
                    $"Multiple writers for the same key aren't allowed: {nameof(key)}={key}");
            }

            RegisterDelegate(_writers, key, writer, preprocess);
        }

        public void UnregisterRead(Action<string> reader) => _readers.Remove(reader);
        public void UnregisterWrite(Func<string> writer) => _writers.Remove(writer);
        public void UnregisterRead(string key, Action<string> reader) => UnregisterDelegate(_readers, key, reader);
        public void UnregisterWrite(string key, Func<string> writer) => UnregisterDelegate(_writers, key, writer);

        void RegisterDelegate<TDelegate>(
            Dictionary<TDelegate, Dictionary<string, bool>> map,
            string key,
            TDelegate callback,
            bool preprocess)
        {
            if (map.TryGetValue(callback, out var keys))
            {
                if (keys.ContainsKey(key))
                    return;
                map[callback].Add(key, preprocess);
            }
            else
            {
                map[callback] = new()
                {
                    {key, preprocess},
                };
            }
        }

        void UnregisterDelegate<TDelegate>(
            Dictionary<TDelegate, Dictionary<string, bool>> map,
            string key,
            TDelegate callback)
        {
            if (map.TryGetValue(callback, out var keys))
            {
                keys.Remove(key);
                if (keys.Count == 0)
                    map.Remove(callback);
            }
        }

        internal void ApplyReaders(IReadOnlyList<string> unrecongnizedSaveStrings)
        {
            if (_readers.Count == 0)
                return;

            var keyReaders = TransposeDelegates(_readers);
            foreach (var saveString in unrecongnizedSaveStrings)
            {
                var splits = Regex.Split(saveString, FieldDelimiter);
                if (splits.Length < 2)
                    continue;
                var key = splits[0];
                if (keyReaders.TryGetValue(key, out var readers))
                {
                    var value = splits[1];

                    string? decodedValue = null;
                    Exception? decodeEx = null;
                    try
                    {
                        decodedValue = Encoding.UTF8.GetString(Convert.FromBase64String(value));
                    }
                    catch (Exception ex)
                    {
                        decodeEx = ex;
                    }

                    foreach (var kvp in readers)
                    {
                        var (reader, preprocess) = (kvp.Key, kvp.Value);

                        if (preprocess && decodeEx is not null)
                        {
                            Debug.LogError(
                                $"{logPrefix} Failed to prepare save string for delegate: "
                                + $"{GetMethodFullName(reader.Method)}, value=\"{value}\": {decodeEx}");
                            continue;
                        }

                        try
                        {
                            if (preprocess)
                            {
                                // XXX Consider invoking the reader callback even in the case of decode error, just
                                // pass in null instead so that the callback knows there was only some issue with
                                // decoding string and that at least save string exists for the supplied key
                                //
                                // This'll allow the callback to handle this case on its end, and if desired it could
                                // also try to manually recover save string data via .Read(preprocess: false)
                                //
                                // On the other hand, is it really worth making the reader callback signature nullable
                                // from Action<string> to Action<string?> for an error that'll rarely ever happen?
                                reader(decodedValue!);
                            }
                            else
                                reader(value);
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError(
                                $"{logPrefix} Delegate threw error while reading save string: "
                                + $"{GetMethodFullName(reader.Method)}: {ex}");
                        }
                    }
                }
            }
        }

        internal void ApplyWriters(List<string> unrecongnizedSaveStrings)
        {
            static string? GetWriterSaveString(Dictionary<Func<string>, bool> writers)
            {
                // There's only a single writer for each key
                var kvp = writers.Single();
                var (writer, preprocess) = (kvp.Key, kvp.Value);

                string? data = null;
                try
                {
                    data = writer();
                }
                catch (Exception ex)
                {
                    Debug.LogError(
                        $"{logPrefix} Delegate threw error while writing save string: "
                        + $"{GetMethodFullName(writer.Method)}: {ex}");
                }

                if (data is null)
                    return null;
                if (preprocess)
                    data = Convert.ToBase64String(Encoding.UTF8.GetBytes(data));

                return data;
            }

            if (_writers.Count == 0)
                return;

            // Update existing keyed saveStrings
            var keyWriters = TransposeDelegates(_writers);
            var existingKeys = new HashSet<string>();
            for (var i = 0; i < unrecongnizedSaveStrings.Count; i++)
            {
                var splits = Regex.Split(unrecongnizedSaveStrings[i], FieldDelimiter);
                if (splits.Length < 1)
                    continue;

                var key = splits[0];
                if (keyWriters.TryGetValue(key, out var writers))
                {
                    existingKeys.Add(key);

                    var data = GetWriterSaveString(writers);
                    if (data is null)
                        continue;

                    // Updating instead of removing all keys that could be written anyways since a writer may fail
                    // leaving with no data
                    unrecongnizedSaveStrings[i] = GetSaveString(key, data);
                }
            }

            // Add new key-value save strings
            foreach (var kvp in keyWriters.Where(t => !existingKeys.Contains(t.Key)))
            {
                var (key, writers) = (kvp.Key, kvp.Value);

                var data = GetWriterSaveString(writers);
                if (data is null)
                    continue;

                // Game will append these to the final string
                unrecongnizedSaveStrings.Add(GetSaveString(key, data));
            }
        }

        static Dictionary<string, Dictionary<TDelegate, bool>> TransposeDelegates<TDelegate>(
            Dictionary<TDelegate, Dictionary<string, bool>> map)
            => map
                .SelectMany(kvp =>
                    kvp.Value.Select(kvp2 =>
                        (str: kvp2.Key, t: (callback: kvp.Key, preprocess: kvp2.Value))))
                .GroupBy(t => t.str, t => t.t)
                .ToDictionary(
                    g => g.Key,
                    g => g.ToDictionary(it => it.callback, it => it.preprocess));
    }

    public class SaveState : SaveGameBase
    {
        public override string ParentDelimiter => "<svA>";
        public override string FieldDelimiter => "<svB>";

        public override List<string>? UnrecognizedSaveStrings
            => Singleton.Game?.GetStorySession?.saveState?.unrecognizedSaveStrings;
    }

    public class MiscWorld : SaveGameBase
    {
        public override string ParentDelimiter => "<mwA>";
        public override string FieldDelimiter => "<mwB>";

        public override List<string>? UnrecognizedSaveStrings
            => Singleton.Game?.GetStorySession?
                .saveState?.miscWorldSaveData?.unrecognizedSaveStrings;
    }

    public class DeathPersistent : SaveGameBase
    {
        public override string ParentDelimiter => "<dpA>";
        public override string FieldDelimiter => "<dpB>";

        public override List<string>? UnrecognizedSaveStrings
            => Singleton.Game?.GetStorySession?
                .saveState?.deathPersistentSaveData?.unrecognizedSaveStrings;
    }

    public class Progression : SaveGameBase
    {
        public override string ParentDelimiter => "<mpdA>";
        public override string FieldDelimiter => "<mpdB>";

        public override List<string>? UnrecognizedSaveStrings
            => Singleton.Game?.GetStorySession?
                .saveState?.progression?.miscProgressionData?.unrecognizedSaveStrings;
    }

    static bool isInit = false;

    static readonly string logPrefix = $"{typeof(SaveGame).FullName}:";

    /// <summary>
    /// Regular save data, whatever that means.
    /// </summary>
    public static SaveState SaveStateData { get; private set; } = new();

    /// <summary>
    /// Campaign-specific save data that requires a non-starvation hibernation in order to be written to the savefile.
    /// In case of a starvation hibernation, it is temporarily written to the memory and is used to initialise the next
    /// cycle.
    /// </summary>
    public static MiscWorld MiscWorldData { get; private set; } = new();

    /// <summary>
    /// Campaign-specific save data that is written to the savefile upon ending the cycle in any way or quitting to the
    /// menu. <br/>
    /// It is read from savefile on every game load.
    /// </summary>
    public static Progression DeathPersistentData { get; private set; } = new();

    /// <summary>
    /// Savefile-specific data that gets retained across savefiles. <br/>
    /// It is read once from savefile per game boot, but written to savefile in the same manner as DeathPersistentData.
    /// </summary>
    public static Progression ProgressionData { get; private set; } = new();

    public static void Init()
    {
        if (isInit)
            return;
        isInit = true;

        Singleton.Init();

        On.SaveState.LoadGame += Hook_SaveState_LoadGame;
        On.SaveState.SaveToString += Hook_SaveState_SaveToString;

        On.MiscWorldSaveData.FromString += Hook_MiscWorldSaveData_FromString;
        On.MiscWorldSaveData.ToString += Hook_MiscWorldSaveData_ToString;

        On.DeathPersistentSaveData.FromString += Hook_DeathPersistentSaveData_FromString;
        On.DeathPersistentSaveData.SaveToString += Hook_DeathPersistentSaveData_SaveToString;

        On.PlayerProgression.MiscProgressionData.FromString += Hook_MiscProgressionData_FromString;
        On.PlayerProgression.MiscProgressionData.ToString += Hook_MiscProgressionData_ToString;
    }

    public static void Reset()
    {
        if (!isInit)
            return;
        isInit = false;

        Singleton.Reset();

        SaveStateData = new();
        On.SaveState.LoadGame -= Hook_SaveState_LoadGame;
        On.SaveState.SaveToString -= Hook_SaveState_SaveToString;

        MiscWorldData = new();
        On.MiscWorldSaveData.FromString -= Hook_MiscWorldSaveData_FromString;
        On.MiscWorldSaveData.ToString -= Hook_MiscWorldSaveData_ToString;

        DeathPersistentData = new();
        On.DeathPersistentSaveData.FromString -= Hook_DeathPersistentSaveData_FromString;
        On.DeathPersistentSaveData.SaveToString -= Hook_DeathPersistentSaveData_SaveToString;

        ProgressionData = new();
        On.PlayerProgression.MiscProgressionData.FromString -= Hook_MiscProgressionData_FromString;
        On.PlayerProgression.MiscProgressionData.ToString -= Hook_MiscProgressionData_ToString;
    }

    static void Hook_SaveState_LoadGame(
        On.SaveState.orig_LoadGame orig,
        global::SaveState self,
        string str,
        RainWorldGame game)
    {
        orig(self, str, game);
        SaveStateData.ApplyReaders(self.unrecognizedSaveStrings);
    }

    static string Hook_SaveState_SaveToString(
        On.SaveState.orig_SaveToString orig,
        global::SaveState self)
    {
        SaveStateData.ApplyWriters(self.unrecognizedSaveStrings);
        return orig(self);
    }

    static void Hook_MiscWorldSaveData_FromString(
        On.MiscWorldSaveData.orig_FromString orig,
        MiscWorldSaveData self,
        string s)
    {
        orig(self, s);
        MiscWorldData.ApplyReaders(self.unrecognizedSaveStrings);
    }

    static string Hook_MiscWorldSaveData_ToString(
        On.MiscWorldSaveData.orig_ToString orig,
        MiscWorldSaveData self)
    {
        MiscWorldData.ApplyWriters(self.unrecognizedSaveStrings);
        return orig(self);
    }

    static void Hook_DeathPersistentSaveData_FromString(
        On.DeathPersistentSaveData.orig_FromString orig,
        DeathPersistentSaveData self,
        string s)
    {
        orig(self, s);
        DeathPersistentData.ApplyReaders(self.unrecognizedSaveStrings);
    }

    static string Hook_DeathPersistentSaveData_SaveToString(
        On.DeathPersistentSaveData.orig_SaveToString orig,
        DeathPersistentSaveData self,
        bool saveAsIfPlayerDied,
        bool saveAsIfPlayerQuit)
    {
        DeathPersistentData.ApplyWriters(self.unrecognizedSaveStrings);
        return orig(self, saveAsIfPlayerDied, saveAsIfPlayerQuit);
    }

    static void Hook_MiscProgressionData_FromString(
        On.PlayerProgression.MiscProgressionData.orig_FromString orig,
        PlayerProgression.MiscProgressionData self,
        string s)
    {
        orig(self, s);
        ProgressionData.ApplyReaders(self.unrecognizedSaveStrings);
    }

    static string Hook_MiscProgressionData_ToString(
        On.PlayerProgression.MiscProgressionData.orig_ToString orig,
        PlayerProgression.MiscProgressionData self)
    {
        ProgressionData.ApplyWriters(self.unrecognizedSaveStrings);
        return orig(self);
    }
}
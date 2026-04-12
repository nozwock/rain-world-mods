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
        // bool is the preprocess flag, base64 encoding/decoding for the value in key-value save string
        // It serves as escaping for game's custom save format special meaning literals like <dpA>, etc.
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
                unrecognizedSaveStrings.Add($"{key}{FieldDelimiter}{encodedValue}");
            }
            else
            {
                unrecognizedSaveStrings.Add($"{key}{FieldDelimiter}{value}");
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

        // TODO: Have common internal impl for Register* (and maybe Unregister*) to dispatch to
        public void RegisterRead(string key, Action<string> reader, bool preprocess = true)
        {
            if (_readers.TryGetValue(reader, out var keys))
            {
                if (keys.ContainsKey(key))
                    return;
                _readers[reader].Add(key, preprocess);
            }
            else
            {
                _readers[reader] = new()
                {
                    {key, preprocess},
                };
            }
        }

        public void RegisterWrite(string key, Func<string> writer, bool preprocess = true)
        {
            if (_writers.TryGetValue(writer, out var keys))
            {
                if (keys.ContainsKey(key))
                    return;
                _writers[writer].Add(key, preprocess);
            }
            else
            {
                _writers[writer] = new()
                {
                    {key, preprocess},
                };
            }
        }

        public void UnregisterRead(Action<string> reader)
        {
            _readers.Remove(reader);
        }

        public void UnregisterWrite(Func<string> writer)
        {
            _writers.Remove(writer);
        }

        public void UnregisterRead(string key, Action<string> reader)
        {
            if (_readers.TryGetValue(reader, out var keys))
            {
                keys.Remove(key);
                if (keys.Count == 0)
                    _readers.Remove(reader);
            }
        }

        public void UnregisterWrite(string key, Func<string> writer)
        {
            if (_writers.TryGetValue(writer, out var keys))
            {
                keys.Remove(key);
                if (keys.Count == 0)
                    _writers.Remove(writer);
            }
        }

        internal void ApplyReaders(IReadOnlyList<string> unrecongnizedSaveStrings)
        {
            // Transform (reader -> (key -> flag)) to (key -> (reader -> flag))
            var keyReaders = _readers
                .SelectMany(kvp =>
                    kvp.Value.Select(kvp2 =>
                        (str: kvp2.Key, t: (reader: kvp.Key, preprocess: kvp2.Value))))
                .GroupBy(t => t.str, t => t.t)
                .ToDictionary(
                    g => g.Key,
                    g => g.ToDictionary(it => it.reader, it => it.preprocess));

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
                        try
                        {
                            if (preprocess)
                            {
                                if (decodeEx is not null)
                                {
                                    throw decodeEx;
                                }

                                // TODO: Still call the reader callback but pass in null so that the consumer knows
                                // there only was some issue with decoding string and that savestring exists for the key
                                //
                                // The callback can then try to manually parse the savestring data via .Read() with
                                // preprocess false if needed
                                reader(decodedValue!);
                            }
                            else
                                reader(value);
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError(
                                $"{logPrefix} {GetMethodFullName(reader.Method)}: Failed to read savedata: {ex}");
                        }
                    }
                }
            }
        }

        internal void ApplyWriters(List<string> unrecongnizedSaveStrings)
        {
            // FIXME: Update instead of remove all first since a writer may fail leaving with no data

            // Remove key-value that are to be updated
            var writerKeys = _writers
                .SelectMany(kvp => kvp.Value.Select(kvp => kvp.Key))
                .ToHashSet();
            unrecongnizedSaveStrings.RemoveAll(saveString =>
            {
                var splits = Regex.Split(saveString, FieldDelimiter);
                if (splits.Length == 0)
                    return false;
                var key = splits[0];
                return writerKeys.Contains(key);
            });

            foreach (var kvp in _writers)
            {
                var (writer, keys) = (kvp.Key, kvp.Value);

                string? writeString = null;
                try
                {
                    writeString = writer();
                }
                catch (Exception ex)
                {
                    Debug.LogError(
                        $"{logPrefix} {GetMethodFullName(writer.Method)}: Failed to write savedata: {ex}");
                }

                if (writeString is null)
                    continue;

                foreach (var kvp2 in keys)
                {
                    var (key, preprocess) = (kvp2.Key, kvp2.Value);

                    // Game will append these to final string
                    if (preprocess)
                    {
                        var encodedWriteString = Convert.ToBase64String(Encoding.UTF8.GetBytes(writeString));
                        unrecongnizedSaveStrings.Add($"{key}{FieldDelimiter}{encodedWriteString}");
                    }
                    else
                    {
                        unrecongnizedSaveStrings.Add($"{key}{FieldDelimiter}{writeString}");
                    }
                }
            }
        }
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

    static readonly string logPrefix = $"{typeof(SaveGame).Namespace}.{nameof(SaveGame)}:";

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

        DeathPersistentData = new();
        On.DeathPersistentSaveData.FromString += Hook_DeathPersistentSaveData_FromString;
        On.DeathPersistentSaveData.SaveToString += Hook_DeathPersistentSaveData_SaveToString;

        ProgressionData = new();
        On.PlayerProgression.MiscProgressionData.FromString -= Hook_MiscProgressionData_FromString;
        On.PlayerProgression.MiscProgressionData.ToString -= Hook_MiscProgressionData_ToString;
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
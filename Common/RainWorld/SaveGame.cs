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
        public void RegisterWrite(string key, Func<string> writer, bool preprocess = true)
            => RegisterDelegate(_writers, key, writer, preprocess);

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
                        try
                        {
                            if (preprocess)
                            {
                                if (decodeEx is not null)
                                {
                                    throw decodeEx;
                                }

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
            if (_writers.Count == 0)
                return;

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
                        $"{logPrefix} Delegate threw error while writing save string: "
                        + $"{GetMethodFullName(writer.Method)}: {ex}");
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
                        unrecongnizedSaveStrings.Add(GetSaveString(key, encodedWriteString));
                    }
                    else
                    {
                        unrecongnizedSaveStrings.Add(GetSaveString(key, writeString));
                    }
                }
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

        Singleton.Reset();

        DeathPersistentData = new();
        On.DeathPersistentSaveData.FromString -= Hook_DeathPersistentSaveData_FromString;
        On.DeathPersistentSaveData.SaveToString -= Hook_DeathPersistentSaveData_SaveToString;

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
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
        protected Dictionary<Action<string>, HashSet<string>> _readers = [];
        protected Dictionary<Func<string>, HashSet<string>> _writers = [];

        public abstract string ParentDelimiter { get; }
        public abstract string FieldDelimiter { get; }

        static string GetMethodFullName(MethodInfo method) => $"{method.DeclaringType.Name}.{method.Name}";

        // TODO: Handle escaping to string data by base64 encoding it
        public void ApplyReaders(IReadOnlyList<string> unrecongnizedSaveStrings)
        {
            var keyReaders = _readers
                .SelectMany(kvp => kvp.Value.Select(str => (str, reader: kvp.Key)))
                .GroupBy(t => t.str, t => t.reader)
                .ToDictionary(g => g.Key, g => g.ToHashSet());

            foreach (var saveString in unrecongnizedSaveStrings)
            {
                var splits = Regex.Split(saveString, FieldDelimiter);
                var key = splits[0];
                if (keyReaders.TryGetValue(key, out var readers))
                {
                    var value = splits[1];
                    foreach (var reader in readers)
                    {
                        try
                        {
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

        public void ApplyWriters(List<string> unrecongnizedSaveStrings)
        {
            // Remove key-value that are to be updated
            var writerKeys = _writers
                .SelectMany(kvp => kvp.Value.Select(str => str))
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

                foreach (var key in keys)
                {
                    // Game will append these to final string
                    unrecongnizedSaveStrings.Add($"{key}{FieldDelimiter}{writeString}");
                }
            }
        }

        // TODO: API for queuing removal of key-value in savedata

        public void RegisterRead(string key, Action<string> reader)
        {
            if (_readers.TryGetValue(reader, out var keys))
            {
                if (keys.Contains(key))
                    return;
                _readers[reader].Add(key);
            }
            else
            {
                _readers[reader] = [key];
            }
        }

        public void RegisterWrite(string key, Func<string> writer)
        {
            if (_writers.TryGetValue(writer, out var keys))
            {
                if (keys.Contains(key))
                    return;
                _writers[writer].Add(key);
            }
            else
            {
                _writers[writer] = [key];
            }
        }

        public void CancelRead(Action<string> reader)
        {
            _readers.Remove(reader);
        }

        public void CancelWrite(Func<string> writer)
        {
            _writers.Remove(writer);
        }

        public void CancelRead(string key, Action<string> reader)
        {
            if (_readers.TryGetValue(reader, out var keys))
            {
                keys.Remove(key);
                if (keys.Count == 0)
                    _readers.Remove(reader);
            }
        }

        public void CancelWrite(string key, Func<string> writer)
        {
            if (_writers.TryGetValue(writer, out var keys))
            {
                keys.Remove(key);
                if (keys.Count == 0)
                    _writers.Remove(writer);
            }
        }
    }

    public class Progression : SaveGameBase
    {
        public override string ParentDelimiter => "<mpdA>";
        public override string FieldDelimiter => "<mpdB>";
    }

    static bool isInit = false;

    static readonly string logPrefix = $"{typeof(SaveGame).Namespace}.{nameof(SaveGame)}:";

    /// <summary>
    /// This SaveData is read only once on game start and written on each cycle and game exit.
    /// </summary>
    public static Progression ProgressionData { get; private set; } = new();

    public static void Init()
    {
        if (isInit)
            return;
        isInit = true;

        On.PlayerProgression.MiscProgressionData.ToString += Hook_MiscProgressionData_ToString;
        On.PlayerProgression.MiscProgressionData.FromString += Hook_MiscProgressionData_FromString;
    }

    public static void Reset()
    {
        if (!isInit)
            return;
        isInit = false;

        ProgressionData = new();
        On.PlayerProgression.MiscProgressionData.ToString -= Hook_MiscProgressionData_ToString;
        On.PlayerProgression.MiscProgressionData.FromString -= Hook_MiscProgressionData_FromString;
    }

    static void Hook_MiscProgressionData_FromString(
        On.PlayerProgression.MiscProgressionData.orig_FromString orig,
        PlayerProgression.MiscProgressionData self,
        string s)
    {
        orig(self, s);
        Debug.Log(
            $"{logPrefix} Reading MiscProgressionData: unrecognizedSaveStrings="
            + self.unrecognizedSaveStrings.Count);
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
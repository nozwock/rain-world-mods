using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using BepInEx.Logging;

namespace Common.BepInEx;

public static class Logging
{
    static readonly FieldInfo fieldConfigConsoleDisplayedLevel = typeof(ConsoleLogListener)
        .GetField("ConfigConsoleDisplayedLevel", BindingFlags.NonPublic | BindingFlags.Static);

    // BepInEx 5.4.17
    // Why're you like this bepinex...
    /// <summary>
    /// Returns current BepInEx LogLevel based on config.
    /// </summary>
    public static LogLevel LogLevel => DiskLogLevel | ConsoleLogLevel;

    public static LogLevel DiskLogLevel
        => Logger.Listeners.OfType<DiskLogListener>().FirstOrDefault()?.DisplayedLogLevel ?? LogLevel.None;

    public static LogLevel ConsoleLogLevel
        => ((ConfigEntry<LogLevel>)fieldConfigConsoleDisplayedLevel.GetValue(null)).Value;
}

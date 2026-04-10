using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using BepInEx.Logging;

namespace Common.BepInEx;

public static class Logging
{
    static readonly FieldInfo fieldConfigConsoleDisplayedLevel = typeof(ConsoleLogListener)
        .GetField("ConfigConsoleDisplayedLevel", BindingFlags.NonPublic | BindingFlags.Static);

    static ConfigEntry<LogLevel> ConfigConsoleDisplayedLevel
        => (ConfigEntry<LogLevel>)fieldConfigConsoleDisplayedLevel.GetValue(null);

    // BepInEx 5.4.17
    // Why're you like this bepinex...
    /// <summary>
    /// Returns current BepInEx LogLevel based on config.
    /// </summary>
    public static LogLevel LogLevel
    {
        get
        {
            var diskLogListener = Logger.Listeners.OfType<DiskLogListener>().FirstOrDefault();
            return (diskLogListener?.DisplayedLogLevel ?? LogLevel.None)
                | ConfigConsoleDisplayedLevel.Value;
        }
    }
}

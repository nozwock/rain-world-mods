using System;

namespace Common.RainWorld;

static class Singleton
{
    static bool isInit = false;

    // https://github.com/SaltiestSyrup/RWRandomizer/blob/e0c8fc4d5170aef556822c053b45a0fb245557bb/Hooks/GameLoopHooks.cs#L200
    static WeakReference<RainWorldGame>? _game;
    public static RainWorldGame? Game
    {
        get
        {
            if (_game is null)
                return null;
            if (_game.TryGetTarget(out var game))
                return game;
            return null;
        }
        private set
        {
            if (value is not null)
                _game = new(value);
        }
    }

    public static void Init()
    {
        if (isInit)
            return;
        isInit = true;

        On.RainWorldGame.ctor += Hook_RainWorldGame_ctor;
    }

    public static void Reset()
    {
        if (!isInit)
            return;
        isInit = false;

        On.RainWorldGame.ctor -= Hook_RainWorldGame_ctor;
    }

    // It works because RainWorldGame will not have being constructed by the time we setup hooks
    // https://discord.com/channels/291184728944410624/1094741201623720058/1405336874024833044
    private static void Hook_RainWorldGame_ctor(
        On.RainWorldGame.orig_ctor orig,
        RainWorldGame self,
        ProcessManager manager)
    {
        Game = self;
    }
}
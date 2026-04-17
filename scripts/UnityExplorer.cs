using RWCustom;

// Symlink it to sinai-dev-UnityExplorer\Script\startup.cs and not UnityExplorer.cs, it doesn't seem to load otherwise

// Not supported by UnityExplorer:
// nullable types
// is, is not null
// implicit new(...)

public static class g
{
    static WeakReference<RainWorldGame> _game;
    public static RainWorldGame Game
    {
        get
        {
            if (_game == null || !_game.TryGetTarget(out var _))
            {
                var game = UnityEngine.Object.FindObjectsOfType<RainWorld>()
                    .Where(it => it.gameObject.name == "Futile")
                    .First()
                    .processManager
                    .currentMainLoop as RainWorldGame;
                if (game != null)
                    _game = new WeakReference<RainWorldGame>(game);
            }

            if (_game == null)
                return null;
            if (_game.TryGetTarget(out var game2))
                return game2;
            return null;
        }
    }

    public static void ResetMap()
    {
        var texture = Game.cameras[0].hud.map.discoverTexture;
        for (int x = 0; x < texture.width; x++)
            for (int y = 0; y < texture.height; y++)
                texture.SetPixel(x, y, new Color(0, 0, 0));
    }
}

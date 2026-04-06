# Notes

Dumping ground for whatever's interesting.

```
RainWorld
    ProcessManager
        RainWorldGame
            Player
```

```cs
var game = UnityEngine.Object.FindObjectsOfType<RainWorld>()
    .Where(it => it.gameObject.name == "Futile")
    .First()
    .processManager
    .currentMainLoop as RainWorldGame;
game.FirstRealizedPlayer.AddFood(7);
```
# Notes

Dumping ground for whatever's interesting.

```
RainWorld
    ProcessManager
        RainWorldGame
            Player
```

```cs
var game = GameObject.Find("Futile")
    .GetComponent<RainWorld>()
    .processManager
    .currentMainLoop as RainWorldGame;
game.FirstRealizedPlayer.AddFood(7);
```
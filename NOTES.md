# Notes

Dumping ground for whatever's interesting.

## Auto Remix Menu
For context, see [Creating a Remix Menu](https://rainworldmodding.miraheze.org/wiki/Creating_a_Remix_Menu).

For `UIQueue` based remix configs, you can specify display string for the auto-generated `OpLabel`s by specifying
translation for the config `key` in `text/text_*/strings.txt`[^1].

> [!WARNING]
> `strings.txt` need to using CRLF for EOL, otherwise things may not work as expected. \
> This very likely applies to all the other data files the game exposes for modding.

## General

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


[^1]: See specific implementations of `Menu.Remix.MixedUI.UIQueue._InitializeThisQueue`, such as
`Menu.Remix.MixedUI.OpCheckBox.Queue._InitializeThisQueue`.

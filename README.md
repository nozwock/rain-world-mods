# Rain World Mods

Mods for [Rain World][rain-world-steam].

## Installation

Extract the contents of mod archives into `<Game Folder>/RainWorld_Data/StreamingAssets/mods/<Mod Name>/`.

After which the mod should be available in the Remix menu to enable.

[See here][raindb-tutorial] for in-depth instructions.

## Build

1. Define `GamePath` property in `Local.props` next to `Directory.Build.props`.
2. Define `MOD_PATH` in an `.env` file in the repo's root.
    ```
    MOD_PATH="/path/to/RainWorld_Data/StreamingAssets/mods/"
    ```
3. Run [just] `install` from within a mod subdirectory to install the mod.


[just]: https://github.com/casey/just/
[rain-world-steam]: https://store.steampowered.com/app/312520/Rain_World/
[raindb-tutorial]: https://andrewfm.github.io/RainDB/tutorials.html

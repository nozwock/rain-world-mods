using System;
using Common.RainWorld.UI;
using Menu.Remix;
using Menu.Remix.MixedUI;
using UnityEngine;

namespace MapRemix;

class RemixConfig : OptionInterface
{
    Color colorRed = new(0.85f, 0.35f, 0.4f);
    Color colorOrange = new(1f, 0.7255f, 0.451f); // cOutdated from Menu.Remix.MenuModList

    public Action? OnResetDiscoveredMapCache;

    public Configurable<bool> cfgInstantDiscoveredAreaReveal = new(true);
    public Configurable<bool> cfgInstantMapReveal = new(true);

    public bool IsInit { get; private set; }

    public RemixConfig(object? _) { }
    public RemixConfig()
    {
        if (IsInit)
            return;

        IsInit = true;

        cfgInstantDiscoveredAreaReveal = config.Bind(
            "InstantDiscoveredAreaReveal",
            defaultValue: cfgInstantDiscoveredAreaReveal.Value,
            new ConfigurableInfo(
                "Reveal all the discovered map areas instantly",
                tags: ["Instant discovered areas reveal"]));

        cfgInstantMapReveal = config.Bind(
            "InstantMapReveal",
            defaultValue: cfgInstantMapReveal.Value,
            new ConfigurableInfo(
                "Minimap will reveal itself instantly",
                tags: ["Instant map reveal"]));
    }

    public override void Initialize()
    {
        base.Initialize();

        var tab = new OpTab(this);
        Tabs = [tab];

        UIQueueEx.InitializeQueues(tab,
        [
            new UIQueueEx.ModifyQueueAll(UIQueueEx.SetButtonAtleastSizeX(120)),

            new UIQueueEx.Spacing(40),
            new OpLabel.Queue("Map Remix", FLabelAlignment.Center, true),
            new UIQueueEx.Spacing(40),

            new OpCheckBox.Queue(cfgInstantDiscoveredAreaReveal),
            new OpCheckBox.Queue(cfgInstantMapReveal),

            new UIQueueEx.Spacing(10),
            new UIQueueEx.ColorNext(colorOrange),
            new OpHoldButton.QueueRectangular("CLEAR", "Clear cache for discovered map areas")
            {
              onPressDone = (_) => {
                ConfigConnector.CreateDialogBoxNotify(
                    Translate("Mod's cache for discovered map areas will be cleared after this prompt, "
                    + "the cache is used to avoid trying to uncover already discovered areas.")
                    + "\n\n"
                    + Translate("To persist the cleared cache in the savefile, you need to enter and exit a game mode."),
                    () => {
                        OnResetDiscoveredMapCache?.Invoke();
                    });
              },
            },
        ]);
    }
}

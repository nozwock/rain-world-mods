using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using Common.RainWorld;
using Common.RainWorld.UI;
using Menu.Remix;
using Menu.Remix.MixedUI;
using UnityEngine;
using UnityEngine.UIElements;

namespace BetterMap;

class RemixConfig : OptionInterface
{
    Color colorRed = new(0.85f, 0.35f, 0.4f);
    Color colorOrange = new(1f, 0.7255f, 0.451f); // cOutdated from Menu.Remix.MenuModList

    public Action? OnResetDiscoveredMapCache;

    public Configurable<bool> cfgInstantDiscoveredAreaReveal = new(true);

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
    }

    public override void Initialize()
    {
        base.Initialize();

        var tab = new OpTab(this);
        Tabs = [tab];

        var offsetY = 0f;
        UIQueueEx.InitializeQueues(tab, ref offsetY, posX: default, spacing: default,
        [
            new UIQueueEx.ModifyQueueAll(UIQueueEx.SetButtonAtleastSizeX(120)),

            new UIQueueEx.Spacing(40),
            new OpLabel.Queue("Better Map", FLabelAlignment.Center, true),
            new UIQueueEx.Spacing(40),

            new OpCheckBox.Queue(cfgInstantDiscoveredAreaReveal),

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

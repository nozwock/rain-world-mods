using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Menu.Remix.MixedUI;
using UnityEngine;

namespace Common.RainWorld.UI;

public static class UIQueueEx
{
    public record struct Spacing(float Y) { }
    public record struct ColorNext(Color Color) { }

    interface IModifyQueueAll
    {
        void TryApply(UIQueue queue, List<UIelement> group);
    }

    public record struct ModifyQueueAll<T>(Action<UIQueue, List<UIelement>> Mutator) : IModifyQueueAll
    {
        public readonly void TryApply(UIQueue queue, List<UIelement> group)
        {
            if (group.OfType<T>().Any())
                Mutator(queue, group);
        }
    }

    public record struct ModifyQueueNext(Action<UIQueue, List<UIelement>> Mutator)
    {
        public readonly void Apply(UIQueue queue, List<UIelement> group) => Mutator(queue, group);
    }

    public static void SetButtonMinLabelSize(UIQueue _, List<UIelement> elems) => SetButtonSizeX(_, elems);

    public static Action<UIQueue, List<UIelement>> SetButtonAtleastSizeX(float x)
    {
        return (queue, elems) =>
        {
            if (x < 0) return;
            SetButtonSizeX(queue, elems, x);
        };
    }

    static void SetButtonSizeX(UIQueue _, List<UIelement> elems, float atleastX = 0)
    {
        var result = elems
            .Select((op, i) => (op, i))
            .FirstOrDefault(t => t.op is OpSimpleButton or OpHoldButton { isRectangular: true });

        var (btn, i) = result;
        if (btn == null) return;

        var text = btn switch
        {
            OpSimpleButton b => b.text,
            OpHoldButton b => b.text,
            _ => null
        };

        SetSizeX(btn, Mathf.Max(atleastX, LabelTest.GetWidth(text) + 20), elems.Skip(i + 1));
    }

    public static Action<UIQueue, List<UIelement>> SetSizeX<T>(float x) where T : UIelement
    {
        return (queue, elems) =>
        {
            if (x < 0) return;
            if (!elems.OfType<T>().Any()) return;

            var (elem, i) = elems.OfType<T>()
                .Select((elem, i) => (elem, i)).First();

            SetSizeX(elem, x, elems.Skip(i + 1));
        };
    }

    public static void InitializeQueues(
        IHoldUIelements holder,
        ref float offsetY,
        float? posX,
        float? spacing,
        params IEnumerable<object> queues)
    {
        posX ??= 20; // InternalOI_Auto.Initialize
        spacing ??= 10;

        List<UIelement> widgets = [];

        bool resizedCanvas = false;
        float sizeY = CalculateSizeY((float)spacing, queues);
        if (holder.CanvasSize.y < sizeY + offsetY)
        {
            resizedCanvas = UIQueue._ResizeCanvas(ref holder, sizeY + offsetY + 10f);
        }

        Color? applyColorOnce = null;
        ModifyQueueNext? applyQueueMutatorOnce = null;
        IModifyQueueAll[] queueMutatorsAll = [.. queues.OfType<IModifyQueueAll>()];
        foreach (var item in queues.Where(it => it is not IModifyQueueAll))
        {
            if (item is Spacing spacing1)
            {
                offsetY += spacing1.Y;
                continue;
            }
            if (item is ColorNext colorNext)
            {
                applyColorOnce = colorNext.Color;
                continue;
            }
            if (item is ModifyQueueNext modifyQueueNext)
            {
                applyQueueMutatorOnce = modifyQueueNext;
                continue;
            }

            if (item is not UIQueue queue)
                continue;

            queue.OnPreInitialize?.Invoke(queue);

            var queueWidgets = queue._InitializeThisQueue(holder, (float)posX, ref offsetY);
            widgets.AddRange(queueWidgets);

            if (queue is UIconfig.ConfigQueue configQueue
                && configQueue.config.info?.Tags is { } tags)
            {
                if (tags.OfType<string>().FirstOrDefault() is { } displayString
                    && !string.IsNullOrEmpty(configQueue.config.key)
                    && queueWidgets.OfType<OpLabel>().FirstOrDefault() is { } label)
                {
                    label.text = OptionInterface.Translate(displayString);
                }

                if (applyColorOnce is null && tags.OfType<Color?>().FirstOrDefault() is { } color1)
                {
                    foreach (var ui in queueWidgets)
                    {
                        SetUIElementColor(ui, color1);
                    }
                }
            }

            foreach (var queueMutator in queueMutatorsAll)
            {
                queueMutator.TryApply(queue, queueWidgets);
            }

            if (applyColorOnce is { } color)
            {
                foreach (var ui in queueWidgets)
                {
                    SetUIElementColor(ui, color);
                }
                applyColorOnce = null;
            }

            if (applyQueueMutatorOnce is { } queueMutator1)
            {
                queueMutator1.Apply(queue, queueWidgets);
                applyQueueMutatorOnce = null;
            }

            queue.OnPostInitialize?.Invoke(queue);

            if (queue.sizeY > 0f)
            {
                offsetY += (float)spacing;
            }
        }

        holder.AddItems([.. widgets]);

        UIfocusable? uIfocusable = null;
        foreach (var queue in queues.OfType<UIfocusable.FocusableQueue>())
        {
            var mainFocusable = queue.mainFocusable;
            if (uIfocusable == null)
            {
                uIfocusable = mainFocusable;
                continue;
            }

            UIfocusable.MutualVerticalFocusableBind(mainFocusable, uIfocusable);
            uIfocusable = mainFocusable;
        }
    }

    static void SetUIElementColor(UIelement ui, Color color)
    {
        var type = ui.GetType();

        var fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(f => f.FieldType == typeof(Color)
                && f.Name.StartsWith("color", StringComparison.OrdinalIgnoreCase));

        foreach (var field in fields)
            field.SetValue(ui, color);
    }

    static float CalculateSizeY(float spacing, params object[] queues)
    {
        float sizeY = 0f;
        foreach (var item in queues)
        {
            sizeY += item switch
            {
                UIQueue queue => queue.sizeY + (queue.sizeY > 0 ? spacing : 0),
                Spacing pad => pad.Y,
                _ => 0,
            };
        }

        return sizeY;
    }

    static void SetSizeX(UIelement elem, float x, IEnumerable<UIelement> elemsAfter)
    {
        if (x < 0) return;

        var size = elem.size;
        var diffX = size.x - x;
        size.x = x;
        elem.size = size;

        foreach (var elemAfter in elemsAfter)
        {
            elemAfter.PosX -= diffX;
        }
    }
}
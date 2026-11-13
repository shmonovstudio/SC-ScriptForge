using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace SC.ScriptForge;

/// <summary>Short, cancellable UI motion. No timers or animation loops remain after completion.</summary>
public static class Motion
{
    private static readonly ConditionalWeakTable<Control, Interaction> Interactions = new();
    private static readonly List<WeakReference<Interaction>> Registered = [];
    private static readonly Dictionary<Control, CancellationTokenSource> Active = [];
    private static bool _reduced;
    public static bool Reduced
    {
        get => _reduced;
        set
        {
            if (_reduced == value) return;
            _reduced = value;
            if (value)
                foreach (var pair in Active.ToArray()) { pair.Value.Cancel(); Reset(pair.Key); }
            for (var i = Registered.Count - 1; i >= 0; i--)
            {
                if (Registered[i].TryGetTarget(out var interaction)) interaction.Configure();
                else Registered.RemoveAt(i);
            }
        }
    }
    public static int ActiveCount => Active.Count;
    public static void Button(Button button) => Track(button, true);
    public static void Card(Border card) => Track(card, false);
    private static void Track(Control control, bool button)
    {
        if (Interactions.TryGetValue(control, out var existing)) { existing.Configure(); return; }
        var interaction = new Interaction(control, button);
        Interactions.Add(control, interaction); Registered.Add(new(interaction));
        if (Registered.Count > 256) Registered.RemoveAll(reference => !reference.TryGetTarget(out _));
    }
    public static void RevealOnOpen(Window window)
    {
        window.Opened += (_, _) => { if (window.Content is Control content) _ = RevealAsync(content, 180, 6); };
    }
    public static void Enter(Control content, bool cascade = false, bool animateRoot = true)
    {
        void Start() => Dispatcher.UIThread.Post(async () =>
        {
            if (!content.IsAttachedToVisualTree() || Reduced) return;
            var tasks = new List<Task>();
            if (animateRoot) tasks.Add(RevealAsync(content, 260, 8));
            if (cascade)
            {
                static bool Candidate(Control item) => item is ListBoxItem || item.Classes.Contains("card") || item.Classes.Contains("motionItem") || item.Classes.Contains("aiSuggestion") || item.Classes.Contains("homeHero");
                var items = content.GetVisualDescendants().OfType<Control>()
                    .Where(c => c.IsEffectivelyVisible && Candidate(c) && !c.GetVisualAncestors().TakeWhile(a => !ReferenceEquals(a, content)).OfType<Control>().Any(Candidate))
                    .Where(c => c.TranslatePoint(default, content) is { } point && point.Y < content.Bounds.Height && point.Y + c.Bounds.Height > 0)
                    .Take(10).ToArray();
                for (var i = 0; i < items.Length; i++) tasks.Add(RevealAsync(items[i], 280, 10, i * 22));
            }
            await Task.WhenAll(tasks);
        }, DispatcherPriority.Loaded);
        if (content.IsAttachedToVisualTree()) Start();
        else
        {
            void Attached(object? sender, VisualTreeAttachmentEventArgs args) { content.AttachedToVisualTree -= Attached; Start(); }
            content.AttachedToVisualTree += Attached;
        }
    }
    public static async Task RevealAsync(Control control, int duration = 180, double distance = 0, int delay = 0)
    {
        Cancel(control);
        if (Reduced || !control.IsAttachedToVisualTree()) { Reset(control); return; }
        control.RenderTransform ??= new TranslateTransform();
        using var source = new CancellationTokenSource(); Active[control] = source;
        void Detached(object? sender, VisualTreeAttachmentEventArgs args) => source.Cancel();
        control.DetachedFromVisualTree += Detached;
        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(duration), Delay = TimeSpan.FromMilliseconds(delay), Easing = new CubicEaseOut(),
            FillMode = FillMode.Backward,
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(Visual.OpacityProperty, 0d), new Setter(TranslateTransform.YProperty, distance) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(Visual.OpacityProperty, 1d), new Setter(TranslateTransform.YProperty, 0d) } }
            }
        };
        try { await animation.RunAsync(control, source.Token); }
        catch (OperationCanceledException) { }
        finally
        {
            control.DetachedFromVisualTree -= Detached;
            if (Active.TryGetValue(control, out var current) && ReferenceEquals(current, source)) { Active.Remove(control); Reset(control); }
        }
    }
    public static async Task GrowAsync(Control bar, int delay = 0)
    {
        if (Reduced || !bar.IsAttachedToVisualTree()) return;
        Cancel(bar);
        using var source = new CancellationTokenSource(); Active[bar] = source;
        void Detached(object? sender, VisualTreeAttachmentEventArgs args) => source.Cancel();
        bar.DetachedFromVisualTree += Detached; bar.RenderTransformOrigin = new RelativePoint(.5, 1, RelativeUnit.Relative);
        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(420), Delay = TimeSpan.FromMilliseconds(delay), Easing = new CubicEaseOut(),
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(ScaleTransform.ScaleYProperty, .06d), new Setter(Visual.OpacityProperty, .25d) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(ScaleTransform.ScaleYProperty, 1d), new Setter(Visual.OpacityProperty, 1d) } }
            }
        };
        try { await animation.RunAsync(bar, source.Token); }
        catch (OperationCanceledException) { }
        finally
        {
            bar.DetachedFromVisualTree -= Detached;
            if (Active.TryGetValue(bar, out var current) && ReferenceEquals(current, source)) { Active.Remove(bar); Reset(bar); }
        }
    }
    private static void Cancel(Control control) { if (Active.TryGetValue(control, out var source)) source.Cancel(); }
    private static void Reset(Control control)
    {
        control.Opacity = 1;
        if (control.RenderTransform is TransformGroup group)
            foreach (var transform in group.Children) ResetTransform(transform);
        else if (control.RenderTransform is Transform transform) ResetTransform(transform);
    }
    private static void ResetTransform(Transform transform)
    {
        if (transform is TranslateTransform translate) { translate.X = 0; translate.Y = 0; }
        if (transform is ScaleTransform scale) { scale.ScaleX = 1; scale.ScaleY = 1; }
    }
    private sealed class Interaction
    {
        private readonly Control _control;
        private readonly bool _button;
        private readonly ScaleTransform _scale = new(1, 1);
        private readonly TranslateTransform _translate = new();
        public Interaction(Control control, bool button)
        {
            _control = control; _button = button;
            control.RenderTransformOrigin = RelativePoint.Center;
            control.RenderTransform = new TransformGroup { Children = { _scale, _translate } };
            control.PointerEntered += (_, _) => Set(false);
            control.PointerExited += (_, _) => Set(false);
            control.DetachedFromVisualTree += (_, _) =>
            {
                _scale.Transitions = null; _translate.Transitions = null;
                _scale.ScaleX = _scale.ScaleY = 1; _translate.Y = 0;
            };
            control.AttachedToVisualTree += (_, _) => Configure();
            if (button)
            {
                control.AddHandler(InputElement.PointerPressedEvent, (_, _) => Set(true), Avalonia.Interactivity.RoutingStrategies.Tunnel);
                control.AddHandler(InputElement.PointerReleasedEvent, (_, _) => Set(false), Avalonia.Interactivity.RoutingStrategies.Tunnel);
                control.PointerCaptureLost += (_, _) => Set(false);
            }
            Configure();
        }
        public void Configure()
        {
            _translate.Transitions = Reduced ? null : new Transitions { new DoubleTransition { Property = TranslateTransform.YProperty, Duration = TimeSpan.FromMilliseconds(170), Easing = new CubicEaseOut() } };
            _scale.Transitions = Reduced ? null : new Transitions
            {
                new DoubleTransition { Property = ScaleTransform.ScaleXProperty, Duration = TimeSpan.FromMilliseconds(140), Easing = new CubicEaseOut() },
                new DoubleTransition { Property = ScaleTransform.ScaleYProperty, Duration = TimeSpan.FromMilliseconds(140), Easing = new CubicEaseOut() }
            };
            _control.Transitions = Reduced ? null : new Transitions
            { new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(150) } };
            if (_control is Button button) button.Transitions = Reduced ? null : new Transitions
            {
                new BrushTransition { Property = Avalonia.Controls.Primitives.TemplatedControl.BackgroundProperty, Duration = TimeSpan.FromMilliseconds(170) },
                new BrushTransition { Property = Avalonia.Controls.Primitives.TemplatedControl.BorderBrushProperty, Duration = TimeSpan.FromMilliseconds(170) }
            };
            if (_control is Border border) border.Transitions = Reduced ? null : new Transitions
            {
                new BrushTransition { Property = Border.BorderBrushProperty, Duration = TimeSpan.FromMilliseconds(190) },
                new BoxShadowsTransition { Property = Border.BoxShadowProperty, Duration = TimeSpan.FromMilliseconds(190) }
            };
            Set(false);
        }
        private void Set(bool pressed)
        {
            var interactive = !Reduced && _control.IsAttachedToVisualTree() && _control.IsEffectivelyEnabled;
            var hovered = interactive && _control.IsPointerOver;
            var anchored = _control.Classes.Contains("nav") || _control.Classes.Contains("tab") || _control.Classes.Contains("link");
            _translate.Y = hovered && !pressed && !anchored ? (_button ? -1 : -2) : 0;
            var size = interactive && pressed && _button ? .97 : hovered && _button && !anchored ? 1.008 : 1;
            _scale.ScaleX = _scale.ScaleY = size;
        }
    }
}

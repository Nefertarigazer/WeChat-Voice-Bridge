using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace VoiceBridge.App;

/// <summary>
/// One contact shows its modes as two wide cards above a wide card; two or more show
/// small cards with capsules. When the count crosses that line the layout changes shape,
/// so instead of jumping it morphs: pictures of the old pieces glide to where their new
/// selves are and fade, while the new pieces glide in from where the old ones stood.
/// The wide mode cards shrink into the remaining card's capsule row (and grow back out).
/// </summary>
public partial class MainWindow
{
    private static readonly Duration MorphTime = new(TimeSpan.FromMilliseconds(340));
    private static readonly IEasingFunction MorphEase = new QuinticEase { EasingMode = EasingMode.EaseOut };
    private const double ShadowRoom = 24;

    private sealed record Shot(FrameworkElement Picture, Rect Bounds);

    private sealed class Before
    {
        public int Count { get; init; }
        public Shot? Modes { get; init; }
        public Rect? CapsuleRow { get; init; }
        public Dictionary<ContactCard, Shot> Cards { get; } = new();
        public Shot? Picker { get; init; }
        public Dictionary<FrameworkElement, Point> Followers { get; } = new();
    }

    /// <summary>Applies a change of the contact list, morphing between the one- and two-contact layouts.</summary>
    private void MorphContacts(Action change)
    {
        var before = CanMorph() ? Capture() : null;
        change();
        if (before is null) return;
        var count = contactCards.Count;
        if (!(before.Count == 1 && count == 2 || before.Count == 2 && count == 1))
        {
            ClearMorph();
            return;
        }
        ContentScroller.UpdateLayout();
        try { Play(before); }
        catch (Exception) { ClearMorph(); /* A failed flourish must never break the list itself. */ }
    }

    private bool CanMorph() => SystemParameters.ClientAreaAnimation && IsLoaded && IsVisible &&
        WindowState != WindowState.Minimized && NavHome.IsChecked == true && contactCards.Count is 1 or 2;

    private Before Capture()
    {
        ClearMorph();
        var before = new Before
        {
            Count = contactCards.Count,
            Modes = contactCards.Count == 1 ? Snap(SingleModes) : null,
            CapsuleRow = contactCards.Count == 2 ? CapsuleRowOf(contactCards[0]) : null,
            Picker = ContactPickerGroup.IsVisible ? Snap(ContactPickerGroup) : null
        };
        if (contactCards.Count == 1 && Snap(SingleContactSurface) is { } single)
            before.Cards[contactCards[0]] = single;
        else
            foreach (var card in contactCards)
                if (ContainerOf(card) is { } container && Snap(container) is { } shot)
                    before.Cards[card] = shot;
        foreach (var follower in new FrameworkElement[] { RecordsHeader, RecordsCard })
            if (follower.IsVisible) before.Followers[follower] = follower.TranslatePoint(new Point(), MorphLayer);
        return before;
    }

    private void Play(Before before)
    {
        var growing = contactCards.Count == 2;
        // The picker closes as the second person is added: let it fade where it stood.
        if (before.Picker is { } picker) Fade(picker.Picture, 1, 0, 0.45);

        foreach (var (card, shot) in before.Cards)
        {
            var target = contactCards.Contains(card) ? growing ? ContainerOf(card) : SingleContactSurface : null;
            if (target is null)
            {
                // Removed: it simply leaves, a little smaller.
                Fade(shot.Picture, 1, 0, 0.55);
                Scale(shot.Picture, shot.Bounds, 1, 0.96);
                continue;
            }
            var bounds = BoundsOf(target);
            Glide(shot.Picture, bounds.TopLeft - shot.Bounds.TopLeft, 0.0, 0.6);
            Fade(shot.Picture, 1, 0, 0.55);
            Arrive(target, shot.Bounds.TopLeft - bounds.TopLeft, 0.12);
        }

        if (growing)
        {
            // The wide mode cards fold into the remaining card's capsule row.
            if (before.Modes is { } modes && contactCards.FirstOrDefault(card => before.Cards.ContainsKey(card)) is { } kept &&
                CapsuleRowOf(kept) is { } row)
            {
                var scale = row.Width / modes.Bounds.Width;
                Glide(modes.Picture, row.TopLeft - modes.Bounds.TopLeft, 0, 1);
                Scale(modes.Picture, modes.Bounds, 1, scale);
                Fade(modes.Picture, 1, 0, 0.7);
            }
            foreach (var card in contactCards.Where(card => !before.Cards.ContainsKey(card)))
                if (ContainerOf(card) is { } added)
                    Arrive(added, new Vector(28, 0), 0.25);
        }
        else if (before.CapsuleRow is { } row)
        {
            // The remaining capsules unfold into the two wide mode cards.
            var bounds = BoundsOf(SingleModes);
            var scale = row.Width / Math.Max(1, bounds.Width);
            Arrive(SingleModes, row.TopLeft - bounds.TopLeft, 0.05, scale);
        }

        foreach (var (follower, was) in before.Followers)
            if (follower.IsVisible)
                Arrive(follower, was - follower.TranslatePoint(new Point(), MorphLayer), 0, fade: false);

        // The latest piece starts a quarter in; give every piece time to land before cleaning up.
        var finish = new System.Windows.Threading.DispatcherTimer { Interval = MorphTime.TimeSpan * 1.25 + TimeSpan.FromMilliseconds(80) };
        finish.Tick += (_, _) => { finish.Stop(); ClearMorph(); };
        finish.Start();
    }

    /// <summary>A picture of the element as it looks now, laid on the morph layer where it stands.</summary>
    private Shot? Snap(FrameworkElement element)
    {
        if (!element.IsVisible || element.ActualWidth < 1 || element.ActualHeight < 1) return null;
        var dpi = VisualTreeHelper.GetDpi(element);
        var width = element.ActualWidth + ShadowRoom * 2;
        var height = element.ActualHeight + ShadowRoom * 2;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width * dpi.DpiScaleX), (int)Math.Ceiling(height * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
            context.DrawRectangle(new VisualBrush(element)
            {
                ViewboxUnits = BrushMappingMode.Absolute, Stretch = Stretch.None,
                Viewbox = new Rect(-ShadowRoom, -ShadowRoom, width, height)
            }, null, new Rect(0, 0, width, height));
        bitmap.Render(drawing);
        bitmap.Freeze();
        var origin = element.TranslatePoint(new Point(), MorphLayer);
        var picture = new Image { Source = bitmap, Width = width, Height = height, IsHitTestVisible = false };
        Canvas.SetLeft(picture, origin.X - ShadowRoom);
        Canvas.SetTop(picture, origin.Y - ShadowRoom);
        MorphLayer.Children.Add(picture);
        return new(picture, new Rect(origin, new Size(element.ActualWidth, element.ActualHeight)));
    }

    private Rect BoundsOf(FrameworkElement element) =>
        new(element.TranslatePoint(new Point(), MorphLayer), new Size(element.ActualWidth, element.ActualHeight));

    private FrameworkElement? ContainerOf(ContactCard card) =>
        ContactsList.ItemContainerGenerator.ContainerFromItem(card) as FrameworkElement;

    /// <summary>Where a card's two capsules sit, on the morph layer.</summary>
    private Rect? CapsuleRowOf(ContactCard card)
    {
        if (ContainerOf(card) is not { } container) return null;
        var capsule = Descendants(container).OfType<ModeCapsule>().FirstOrDefault();
        return capsule?.Parent is FrameworkElement row && row.IsVisible ? BoundsOf(row) : null;
    }

    /// <summary>The new element starts where the old one stood and glides home, fading in.</summary>
    private void Arrive(FrameworkElement element, Vector from, double delay, double scale = 1, bool fade = true)
    {
        var shift = new TranslateTransform(from.X, from.Y);
        var size = new ScaleTransform(scale, scale);
        element.RenderTransform = new TransformGroup { Children = { size, shift } };
        element.RenderTransformOrigin = new Point(0, 0);
        var begin = TimeSpan.FromMilliseconds(MorphTime.TimeSpan.TotalMilliseconds * delay);
        shift.BeginAnimation(TranslateTransform.XProperty, Motion(0, begin));
        shift.BeginAnimation(TranslateTransform.YProperty, Motion(0, begin));
        if (scale != 1)
        {
            size.BeginAnimation(ScaleTransform.ScaleXProperty, Motion(1, begin));
            size.BeginAnimation(ScaleTransform.ScaleYProperty, Motion(1, begin));
        }
        if (fade)
        {
            // Hidden until its delayed fade starts, not shown at full strength meanwhile.
            element.Opacity = 0;
            element.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, MorphTime) { BeginTime = begin, EasingFunction = MorphEase });
        }
    }

    private static void Glide(FrameworkElement picture, Vector by, double delay, double portion)
    {
        var shift = picture.RenderTransform as TransformGroup is { } group ? group.Children.OfType<TranslateTransform>().First() : Attach(picture).shift;
        var duration = new Duration(TimeSpan.FromMilliseconds(MorphTime.TimeSpan.TotalMilliseconds * portion));
        var begin = TimeSpan.FromMilliseconds(MorphTime.TimeSpan.TotalMilliseconds * delay);
        shift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(by.X, duration) { BeginTime = begin, EasingFunction = MorphEase });
        shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(by.Y, duration) { BeginTime = begin, EasingFunction = MorphEase });
    }

    /// <summary>Uniform scale of a picture about the top-left corner of the element it shows.</summary>
    private static void Scale(FrameworkElement picture, Rect bounds, double from, double to)
    {
        var size = picture.RenderTransform as TransformGroup is { } group ? group.Children.OfType<ScaleTransform>().First() : Attach(picture).size;
        size.CenterX = ShadowRoom;
        size.CenterY = ShadowRoom;
        _ = bounds;
        size.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(from, to, MorphTime) { EasingFunction = MorphEase });
        size.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(from, to, MorphTime) { EasingFunction = MorphEase });
    }

    private static void Fade(FrameworkElement picture, double from, double to, double portion) =>
        picture.BeginAnimation(OpacityProperty, new DoubleAnimation(from, to,
            new Duration(TimeSpan.FromMilliseconds(MorphTime.TimeSpan.TotalMilliseconds * portion))) { EasingFunction = MorphEase });

    private static (ScaleTransform size, TranslateTransform shift) Attach(FrameworkElement picture)
    {
        var size = new ScaleTransform(1, 1);
        var shift = new TranslateTransform();
        picture.RenderTransform = new TransformGroup { Children = { size, shift } };
        return (size, shift);
    }

    private static DoubleAnimation Motion(double to, TimeSpan begin) =>
        new(to, MorphTime) { BeginTime = begin, EasingFunction = MorphEase };

    /// <summary>Removes every picture and hands the real elements back their plain layout.</summary>
    private void ClearMorph()
    {
        MorphLayer.Children.Clear();
        var elements = new List<FrameworkElement> { SingleModes, SingleContactSurface, RecordsHeader, RecordsCard };
        elements.AddRange(contactCards.Select(ContainerOf).OfType<FrameworkElement>());
        foreach (var element in elements)
        {
            element.BeginAnimation(OpacityProperty, null);
            element.Opacity = 1;
            element.RenderTransform = Transform.Identity;
        }
    }
}

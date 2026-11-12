using System;
using System.Security;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace VoiceBridge.App;

/// <summary>
/// A collapsible section for dialogs ("高级"). The stock Expander snaps open in one
/// frame and draws a dated circled arrow; this one is a quiet header line whose
/// chevron turns while the body unfolds:
/// <list type="bullet">
/// <item>the body's height eases from 0 to its measured size (ease-out-quint), clipped,
/// so the fields slide into the space instead of the dialog jumping;</item>
/// <item>the fields fade in a beat later and drift down a few pixels, which reads as
/// "revealed from under the header";</item>
/// <item>the dialog grows from its top edge during the motion (no re-centring per
/// frame, which is what makes a resizing window wobble);</item>
/// <item>once open, the scroll area glides just far enough to show the new fields.</item>
/// </list>
/// Collapsing runs the same path back, shorter. With system animations turned off
/// the section simply opens and closes.
/// </summary>
internal static class Disclosure
{
    private static readonly TimeSpan Open = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan Close = TimeSpan.FromMilliseconds(200);

    public static FrameworkElement Create(string title, FrameworkElement body, ScrollViewer viewer)
    {
        var header = new ToggleButton
        {
            Template = HeaderTemplate(title), Cursor = System.Windows.Input.Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Stretch, Focusable = true,
            // WPF draws a focus visual only for keyboard focus, so a mouse click leaves
            // the header plain while Tab still shows where focus is.
            FocusVisualStyle = KeyboardRing
        };
        System.Windows.Automation.AutomationProperties.SetName(header, title + "设置");
        var shift = new TranslateTransform();
        body.RenderTransform = shift;
        var host = new Border { ClipToBounds = true, Height = 0, Visibility = Visibility.Collapsed, Child = body };
        var section = new StackPanel { Margin = new(0, 20, 0, 0) };
        section.Children.Add(header);
        section.Children.Add(host);

        var generation = 0;
        header.Checked += (_, _) => Expand(++generation);
        header.Unchecked += (_, _) => Collapse(++generation);
        return section;

        void Expand(int run)
        {
            host.Visibility = Visibility.Visible;
            var width = section.ActualWidth > 0 ? section.ActualWidth : viewer.ViewportWidth;
            body.Measure(new Size(Math.Max(1, width), double.PositiveInfinity));
            var target = body.DesiredSize.Height;
            if (!SystemParameters.ClientAreaAnimation)
            {
                Settle();
                return;
            }
            var window = Window.GetWindow(section);
            DialogShell.Hold(window, true);
            var grow = new DoubleAnimation(target, Open) { EasingFunction = new QuinticEase { EasingMode = EasingMode.EaseOut } };
            grow.Completed += (_, _) =>
            {
                if (run != generation) return;
                DialogShell.Hold(window, false);
                Settle();
                Reveal();
            };
            host.BeginAnimation(FrameworkElement.HeightProperty, grow);
            body.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))
            {
                BeginTime = TimeSpan.FromMilliseconds(70), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
            shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-8, 0, Open)
            {
                EasingFunction = new QuinticEase { EasingMode = EasingMode.EaseOut }
            });
        }

        // After the motion the section goes back to automatic height, so fields that
        // appear or wrap later still get the room they need.
        void Settle()
        {
            host.BeginAnimation(FrameworkElement.HeightProperty, null);
            host.Height = double.NaN;
            body.BeginAnimation(UIElement.OpacityProperty, null);
            body.Opacity = 1;
            shift.BeginAnimation(TranslateTransform.YProperty, null);
            shift.Y = 0;
        }

        void Collapse(int run)
        {
            var current = host.ActualHeight;
            host.BeginAnimation(FrameworkElement.HeightProperty, null);
            host.Height = current;
            if (!SystemParameters.ClientAreaAnimation || current <= 0)
            {
                Hide();
                return;
            }
            var window = Window.GetWindow(section);
            DialogShell.Hold(window, true);
            var shrink = new DoubleAnimation(current, 0, Close) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } };
            shrink.Completed += (_, _) =>
            {
                if (run != generation) return;
                DialogShell.Hold(window, false);
                Hide();
            };
            host.BeginAnimation(FrameworkElement.HeightProperty, shrink);
            body.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(120)));
            shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-6, Close));
        }

        void Hide()
        {
            host.BeginAnimation(FrameworkElement.HeightProperty, null);
            host.Height = 0;
            host.Visibility = Visibility.Collapsed;
            body.BeginAnimation(UIElement.OpacityProperty, null);
            body.Opacity = 0;
            shift.BeginAnimation(TranslateTransform.YProperty, null);
            shift.Y = 0;
        }

        // Scroll just enough to show the opened fields, without pushing the header
        // itself out of sight.
        void Reveal()
        {
            viewer.UpdateLayout();
            if (!viewer.IsAncestorOf(section)) return;
            var top = section.TransformToAncestor(viewer).Transform(new Point()).Y;
            var bottom = top + section.ActualHeight;
            if (bottom <= viewer.ViewportHeight + 0.5) return;
            var by = Math.Min(bottom - viewer.ViewportHeight + 4, Math.Max(0, top));
            SmoothScroll.To(viewer, viewer.VerticalOffset + by);
        }
    }

    private static readonly Style KeyboardRing = (Style)XamlReader.Parse("""
        <Style TargetType="Control" xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
          <Setter Property="Template">
            <Setter.Value>
              <ControlTemplate>
                <Border Margin="-6,-1,-6,-1" CornerRadius="7" BorderThickness="1.5" BorderBrush="{DynamicResource AccentBorderStrong}"/>
              </ControlTemplate>
            </Setter.Value>
          </Setter>
        </Style>
        """);

    /// <summary>The header line: title, a chevron that turns, and a hairline to the edge.</summary>
    private static ControlTemplate HeaderTemplate(string title) => (ControlTemplate)XamlReader.Parse($$"""
        <ControlTemplate TargetType="ToggleButton"
                         xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <Grid Background="Transparent" Height="30">
            <Grid.ColumnDefinitions>
              <ColumnDefinition Width="Auto"/><ColumnDefinition Width="Auto"/><ColumnDefinition Width="*"/>
            </Grid.ColumnDefinitions>
            <TextBlock x:Name="Title" Text="{{SecurityElement.Escape(title)}}" FontSize="13" Foreground="{DynamicResource Secondary}" VerticalAlignment="Center"/>
            <Path x:Name="Chevron" Grid.Column="1" Data="M 0 0 L 4.5 4.5 L 9 0" Stroke="{DynamicResource Secondary}" StrokeThickness="1.4"
                  StrokeStartLineCap="Round" StrokeEndLineCap="Round" StrokeLineJoin="Round"
                  Width="9" Height="4.5" Margin="8,1,0,0" VerticalAlignment="Center" RenderTransformOrigin="0.5,0.5">
              <Path.RenderTransform><RotateTransform x:Name="Turn" Angle="0"/></Path.RenderTransform>
            </Path>
            <Border x:Name="Rule" Grid.Column="2" Height="1" Background="{DynamicResource Separator}" Margin="12,1,0,0" VerticalAlignment="Center"/>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
              <Setter TargetName="Title" Property="Foreground" Value="{DynamicResource Label}"/>
              <Setter TargetName="Chevron" Property="Stroke" Value="{DynamicResource Label}"/>
              <Setter TargetName="Rule" Property="Background" Value="{DynamicResource ControlBorderStrong}"/>
            </Trigger>
            <Trigger Property="IsChecked" Value="True">
              <Trigger.EnterActions>
                <BeginStoryboard>
                  <Storyboard>
                    <DoubleAnimation Storyboard.TargetName="Turn" Storyboard.TargetProperty="Angle" To="180" Duration="0:0:0.26">
                      <DoubleAnimation.EasingFunction><CubicEase EasingMode="EaseInOut"/></DoubleAnimation.EasingFunction>
                    </DoubleAnimation>
                  </Storyboard>
                </BeginStoryboard>
              </Trigger.EnterActions>
              <Trigger.ExitActions>
                <BeginStoryboard>
                  <Storyboard>
                    <DoubleAnimation Storyboard.TargetName="Turn" Storyboard.TargetProperty="Angle" To="0" Duration="0:0:0.2">
                      <DoubleAnimation.EasingFunction><CubicEase EasingMode="EaseInOut"/></DoubleAnimation.EasingFunction>
                    </DoubleAnimation>
                  </Storyboard>
                </BeginStoryboard>
              </Trigger.ExitActions>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
        """);
}

using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace NML.App.Views;

/// <summary>
/// A PCL-style splash screen: a borderless centered window with the launcher logo + name,
/// fading in over 0.3s, staying for 1.2s, then fading out over 0.4s. Closes itself.
/// </summary>
public sealed class SplashScreenWindow : Window
{
    public SplashScreenWindow()
    {
        Width = 480;
        Height = 280;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SystemDecorations = SystemDecorations.None;
        Background = new SolidColorBrush(Color.FromRgb(15, 15, 20));
        CanResize = false;
        ShowInTaskbar = false;
        Opacity = 0;

        Content = new Border
        {
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(Color.FromRgb(20, 20, 28)),
            Padding = new Thickness(48),
            Child = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Spacing = 16,
                Children =
                {
                    new TextBlock
                    {
                        Text = "🎮",
                        FontSize = 56,
                        HorizontalAlignment = HorizontalAlignment.Center,
                    },
                    new TextBlock
                    {
                        Text = "Next Minecraft Launcher",
                        FontSize = 22,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = Brushes.White,
                        HorizontalAlignment = HorizontalAlignment.Center,
                    },
                    new TextBlock
                    {
                        Text = "AI-powered",
                        FontSize = 13,
                        Foreground = new SolidColorBrush(Color.FromRgb(79, 195, 247)),
                        HorizontalAlignment = HorizontalAlignment.Center,
                    },
                    new ProgressBar
                    {
                        IsIndeterminate = true,
                        Width = 280,
                        Height = 4,
                        Margin = new Thickness(0, 8, 0, 0),
                    },
                },
            },
        };
    }

    /// <summary>Play the fade-in → hold → fade-out sequence, then close.</summary>
    public async Task PlayAsync()
    {
        // Fade in
        await RunFade(0, 1, TimeSpan.FromMilliseconds(300));
        // Hold
        await Task.Delay(1200);
        // Fade out
        await RunFade(1, 0, TimeSpan.FromMilliseconds(400));
        Close();
    }

    private Task RunFade(double from, double to, TimeSpan duration)
    {
        var animation = new Animation
        {
            Duration = duration,
            IterationCount = IterationCount.Parse("1"),
            Children =
            {
                new KeyFrame
                {
                    Setters = { new Setter(OpacityProperty, from) },
                    KeyTime = TimeSpan.Zero,
                },
                new KeyFrame
                {
                    Setters = { new Setter(OpacityProperty, to) },
                    KeyTime = duration,
                },
            },
        };
        return animation.RunAsync(this);
    }
}

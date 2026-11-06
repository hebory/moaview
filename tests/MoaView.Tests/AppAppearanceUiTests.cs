using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using MoaView.Models;
using MoaView.Services;
using MoaView.Tests.TestHelpers;
using MoaView.ViewModels;
using Xunit;

namespace MoaView.Tests;

public sealed class AppAppearanceUiTests
{
    private sealed class MemoryThemeSettings(AppTheme initialTheme) : IAppThemeSettings
    {
        public AppTheme Theme { get; private set; } = initialTheme;
        public int SaveCount { get; private set; }

        public AppTheme Load() => Theme;

        public bool TrySave(AppTheme theme)
        {
            Theme = theme;
            SaveCount++;
            return true;
        }
    }

    [Fact]
    public void SettingsThemePicker_ChangesPaletteAcrossWindowsAndPersistsWithoutDiskAccess()
    {
        StaTestRunner.Run(() =>
        {
            var settings = new MemoryThemeSettings(AppTheme.Dark);
            var appearance = new AppAppearance(settings);
            var first = new MainWindow(new MainWindowViewModel(), appearance: appearance)
            { ShowActivated = false, ShowInTaskbar = false };
            var second = new MainWindow(new MainWindowViewModel(), appearance: appearance)
            { ShowActivated = false, ShowInTaskbar = false };

            try
            {
                first.Show();
                second.Show();
                Pump(first);
                Pump(second);

                var firstSettingsButton = (Button)first.FindName("SettingsButton")!;
                Assert.Equal("설정", AutomationProperties.GetName(firstSettingsButton));
                firstSettingsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Pump(first);
                ((Button)second.FindName("SettingsButton")!).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Pump(second);

                var firstPopup = (Popup)first.FindName("SettingsPopup")!;
                var secondPopup = (Popup)second.FindName("SettingsPopup")!;
                var firstPicker = (ListBox)first.FindName("AppThemePicker")!;
                var secondPicker = (ListBox)second.FindName("AppThemePicker")!;
                Assert.True(firstPopup.IsOpen);
                Assert.True(secondPopup.IsOpen);
                Assert.Equal("앱 테마", AutomationProperties.GetName(firstPicker));
                Assert.Equal(5, firstPicker.Items.Count);
                Assert.Equal("다크", AutomationProperties.GetName(
                    Assert.IsAssignableFrom<ListBoxItem>(firstPicker.ItemContainerGenerator.ContainerFromIndex(0))));

                Select(AppTheme.Light);
                Select(AppTheme.Forest);
                Select(AppTheme.Ocean);

                Assert.Equal(AppTheme.Ocean, appearance.Theme);
                Assert.Equal(AppTheme.Ocean, settings.Theme);
                Assert.Equal(3, settings.SaveCount);
                Assert.Equal(AppTheme.Ocean, firstPicker.SelectedValue);
                Assert.Equal(AppTheme.Ocean, secondPicker.SelectedValue);
                Assert.Equal("", ((TextBlock)first.FindName("AppThemeSaveMessage")!).Text);
                Assert.Equal(Visibility.Collapsed, ((TextBlock)second.FindName("AppThemeSaveMessage")!).Visibility);
                AssertPalette(first, firstPopup);
                AssertPalette(second, secondPopup);

                void Select(AppTheme theme)
                {
                    var previous = Assert.IsType<SolidColorBrush>(first.Background).Color;
                    firstPicker.SelectedValue = theme;
                    Pump(first);
                    Pump(second);
                    Assert.Equal(theme, appearance.Theme);
                    Assert.Equal(theme, secondPicker.SelectedValue);
                    Assert.NotEqual(previous, Assert.IsType<SolidColorBrush>(first.Background).Color);
                    AssertPalette(first, firstPopup);
                    AssertPalette(second, secondPopup);
                }
            }
            finally
            {
                first.Close();
                second.Close();
            }
        });
    }

    private static void AssertPalette(MainWindow window, Popup popup)
    {
        AssertBrushMatches(window, window.Background, "AppWindowBrush");
        AssertBrushMatches(window, ((Border)window.FindName("ViewerToolbar")!).Background, "AppWindowBrush");
        AssertBrushMatches(window, ((Border)popup.Child).Background, "AppPanelBrush");
    }

    private static void AssertBrushMatches(FrameworkElement scope, Brush? actual, string resourceKey)
    {
        var expected = Assert.IsType<SolidColorBrush>(scope.FindResource(resourceKey));
        var brush = Assert.IsType<SolidColorBrush>(actual);
        Assert.Equal(expected.Color, brush.Color);
    }

    private static void Pump(Window window)
    {
        window.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
    }
}

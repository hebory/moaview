using System.IO;
using MoaView.Models;
using MoaView.Services;
using MoaView.Tests.TestHelpers;
using Xunit;

namespace MoaView.Tests;

public sealed class AppThemeSettingsTests
{
    [Fact]
    public void Settings_RoundTripAndUseDarkForMalformedOrInvalidValues()
    {
        using var temp = new TempTestDirectory();
        var path = temp.GetPath("preferences/app-theme.json");
        var settings = new FileAppThemeSettings(path);

        Assert.Equal(AppTheme.Dark, settings.Load());
        Assert.True(settings.TrySave(AppTheme.Ocean));
        Assert.Equal(AppTheme.Ocean, new FileAppThemeSettings(path).Load());
        Assert.False(settings.TrySave((AppTheme)99));

        File.WriteAllText(path, "{\"theme\":\"Unknown\"}");
        Assert.Equal(AppTheme.Dark, settings.Load());
        File.WriteAllText(path, "broken json");
        Assert.Equal(AppTheme.Dark, settings.Load());
    }
}

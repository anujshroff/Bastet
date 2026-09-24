namespace Bastet.Tests.HostIpManagement;

public class AllHostIpsTempDataTests
{
    private static string ReadView(string relativePath)
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Bastet.sln")))
        {
            dir = dir.Parent;
        }

        return File.ReadAllText(Path.Combine(
            dir?.FullName ?? throw new InvalidOperationException("Bastet.sln not found above test base directory"),
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    [Fact]
    public void AllHostIps_ShowsTheBannerAHostIpDeleteSendsThere_WhateverTheListHolds()
    {
        string[] lines = ReadView("src/Bastet/Views/HostIp/AllHostIps.cshtml").Replace("\r", "").Split('\n');
        const string alerts = "@await Html.PartialAsync(\"_TempDataAlerts\")";

        Assert.Single(lines, l => l.Contains("_TempDataAlerts", StringComparison.Ordinal));
        int at = Array.IndexOf(lines, alerts);
        Assert.True(at >= 0, "AllHostIps.cshtml must render the TempData alerts partial on a line of its own, ungated.");
        Assert.True(
            at < Array.FindIndex(lines, l => l.StartsWith("@if (Model.TotalCount == 0)", StringComparison.Ordinal)),
            "The TempData alerts must render before, not inside, the list's empty/non-empty branches.");
    }
}

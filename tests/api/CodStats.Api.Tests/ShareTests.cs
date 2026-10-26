using System.Net;
using System.Net.Http.Json;
using CodStats.Contracts;

namespace CodStats.Api.Tests;

[Collection(ApiTestGroup.Name)]
public class ShareTests(ApiFixture fixture)
{
    private ApiFactory Api => fixture.Api;

    private static string UniqueName(string prefix) => prefix + Guid.NewGuid().ToString("N")[..8];

    private async Task<string> Preview(string path)
    {
        var response = await Api.Anonymous().GetAsync("/api/v1/public/share" + path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        return await response.Content.ReadAsStringAsync();
    }

    [Fact]
    public async Task Profile_preview_shows_name_and_summed_numbers()
    {
        var name = UniqueName("Share");
        using var device = await TestDevice.PublicPlayerAsync(Api, name);
        await device.UploadAsync(Matches.Mw4(30, 10), Matches.Mw4(10, 10, MatchResult.Loss));

        var html = await Preview($"/player/{device.ProfileId}");

        Assert.Contains($"<meta property=\"og:title\" content=\"{name} · COD Stats\" />", html, StringComparison.Ordinal);
        // K/D from the sums (40 / 20), not the average of 3,00 and 1,00 (which would also be 2,00, so: win rate too).
        Assert.Contains("K/D 2,00 · 50 % Siege · 2 Matches · 40 Kills", html, StringComparison.Ordinal);
        Assert.Contains($"/player/{device.ProfileId}\" />", html, StringComparison.Ordinal);
        Assert.Contains("/og.png", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Hidden_or_unknown_profiles_get_the_general_preview()
    {
        var name = UniqueName("Secret");
        using var device = await TestDevice.RegisterAsync(Api);
        await device.Http.PutAsJsonAsync("/api/v1/me/display-name", new SetDisplayNameRequest(name, NameStatus.Confirmed));
        var match = Matches.Mw4(12, 3);
        await device.UploadAsync(match);

        foreach (var path in new[] { $"/player/{device.ProfileId}", $"/player/{device.ProfileId}/match/{match.Id}", $"/player/{Guid.NewGuid()}" })
        {
            var html = await Preview(path);
            Assert.DoesNotContain(name, html, StringComparison.Ordinal);
            Assert.Contains("<meta property=\"og:title\" content=\"COD Stats\" />", html, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Match_preview_describes_the_match_and_encodes_what_players_typed()
    {
        var name = "<b>\"" + UniqueName("X") + "&";
        using var device = await TestDevice.PublicPlayerAsync(Api, name);
        var match = Matches.Mw4(23, 12) with { Mode = "<script>alert(1)</script>" };
        await device.UploadAsync(match);

        var html = await Preview($"/player/{device.ProfileId}/match/{match.Id}");

        Assert.DoesNotContain("<script>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<b>", html, StringComparison.Ordinal);
        Assert.Contains("&lt;b&gt;&quot;", html, StringComparison.Ordinal);
        Assert.Contains("Sieg · &lt;script&gt;", html, StringComparison.Ordinal);
        Assert.Contains("23 Kills · 12 Tode · K/D 1,92 · 2.500 Punkte. MW4 Multiplayer, Test Map.", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Another_players_match_id_gives_no_preview()
    {
        using var owner = await TestDevice.PublicPlayerAsync(Api, UniqueName("Owner"));
        using var other = await TestDevice.PublicPlayerAsync(Api, UniqueName("Other"));
        var match = Matches.Mw4(5, 5);
        await owner.UploadAsync(match);

        var html = await Preview($"/player/{other.ProfileId}/match/{match.Id}");

        Assert.Contains("<meta property=\"og:title\" content=\"COD Stats\" />", html, StringComparison.Ordinal);
    }
}

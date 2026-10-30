using System.Text.Json;
using HalalBook.Api.Services;
using HalalBook.Shared;

namespace HalalBook.Api.Tests;

public class IslamicServiceTests
{
    [Fact]
    public void PrayerTimes_AreInOrder_ForMakkah()
    {
        var t = PrayerTimeCalculator.Calculate(new DateOnly(2026, 3, 20), 21.4225, 39.8262, 180, "Makkah", "Shafi");
        Assert.True(t.Fajr < t.Sunrise && t.Sunrise < t.Dhuhr && t.Dhuhr < t.Asr && t.Asr < t.Maghrib && t.Maghrib < t.Isha);
        // Near the equinox, Makkah's Dhuhr is a little after 12:20 local time.
        Assert.InRange(t.Dhuhr, new TimeOnly(12, 15), new TimeOnly(12, 35));
        // Umm al-Qura: Isha is 90 minutes after Maghrib.
        Assert.Equal(90, (t.Isha - t.Maghrib).TotalMinutes, 1);
    }

    [Fact]
    public void HanafiAsr_IsLaterThanStandard()
    {
        var shafi = PrayerTimeCalculator.Calculate(new DateOnly(2026, 6, 1), 23.81, 90.41, 360, "Karachi", "Shafi");
        var hanafi = PrayerTimeCalculator.Calculate(new DateOnly(2026, 6, 1), 23.81, 90.41, 360, "Karachi", "Hanafi");
        Assert.True(hanafi.Asr > shafi.Asr);
    }

    [Fact]
    public void HighLatitude_StillProducesTimes()
    {
        var t = PrayerTimeCalculator.Calculate(new DateOnly(2026, 6, 21), 59.91, 10.75, 120, "MWL", null); // Oslo, midsummer
        Assert.NotEqual(TimeOnly.MinValue, t.Isha);
        Assert.NotEqual(TimeOnly.MinValue, t.Fajr);
    }

    [Theory]
    [InlineData(43.65, -79.38, 54.6)]   // Toronto
    [InlineData(51.5074, -0.1278, 119)] // London
    [InlineData(-6.2, 106.85, 295)]     // Jakarta
    public void Qibla_Bearing_IsCorrect(double lat, double lng, double expected)
    {
        Assert.Equal(expected, QiblaCalculator.Calculate(lat, lng).Bearing, 0);
    }

    [Fact]
    public void Hijri_KnownDate()
    {
        // 1 Ramadan 1447 AH (Umm al-Qura) is 18 February 2026.
        var h = HijriService.ToHijri(new DateOnly(2026, 2, 18));
        Assert.Equal((1447, 9, 1), (h.Year, h.Month, h.Day));
        Assert.Equal("Ramadan", h.MonthName);
        Assert.Contains("First day of Ramadan", HijriService.OccasionsFor(h));
    }

    [Fact]
    public void Moderator_BlocksAndFlags()
    {
        var m = new ContentModerator(new ModerationOptions { BlockedTerms = ["porn"], ReviewTerms = ["casino"] });
        Assert.False(m.Check("free PORN here").Allowed);
        var review = m.Check("Is the new casino open?");
        Assert.True(review.Allowed);
        Assert.True(review.NeedsReview);
        Assert.Equal(new ModerationResult(true, false, []).Allowed, m.Check("Jumu'ah Mubarak").Allowed);
        Assert.False(m.Check("Jumu'ah Mubarak").NeedsReview);
    }
}

public class MediaSignatureTests
{
    private static Microsoft.AspNetCore.Http.IFormFile File(byte[] bytes) =>
        new Microsoft.AspNetCore.Http.FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", "x");

    [Fact]
    public async Task AcceptsRealMedia_RejectsFakes()
    {
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0, 0, 0, 0, 0 };
        var mp4 = new byte[] { 0, 0, 0, 0x18, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'m', (byte)'p', (byte)'4', (byte)'2', 0, 0, 0, 0 };
        var script = System.Text.Encoding.ASCII.GetBytes("<script>alert(1)</script>");
        Assert.True(await HalalBook.Api.Endpoints.PlatformEndpoints.HasValidSignatureAsync(File(png), MediaType.Image));
        Assert.True(await HalalBook.Api.Endpoints.PlatformEndpoints.HasValidSignatureAsync(File(mp4), MediaType.Video));
        Assert.False(await HalalBook.Api.Endpoints.PlatformEndpoints.HasValidSignatureAsync(File(script), MediaType.Image));
        Assert.False(await HalalBook.Api.Endpoints.PlatformEndpoints.HasValidSignatureAsync(File(png), MediaType.Video));
    }
}

public class VideoDurationTests
{
    [Fact]
    public void ReadsMp4MovieHeaderDuration()
    {
        // ... size | "mvhd" | version 0 | flags | created | modified | timescale=1000 | duration=125000 (125 s)
        var bytes = new byte[64];
        "mvhd"u8.CopyTo(bytes.AsSpan(8));
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(12 + 12), 1000);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(12 + 16), 125_000);
        Assert.Equal(125, MediaPolicy.Mp4Duration(bytes));
    }
}

public class NormalizationTests
{
    [Theory]
    [InlineData("p0rn", "porn")]
    [InlineData("s e x", "sex")]
    [InlineData("N.U.D.E", "nude")]
    [InlineData("Jumu'ah Mubarak", "jumu'ah mubarak")]
    public void UndoesFilterDodges(string input, string expected) => Assert.Equal(expected, ContentModerator.Normalize(input));
}

public class LanguageTests
{
    [Theory]
    [InlineData("السلام عليكم", "ar")]
    [InlineData("آپ کیسے ہیں؟ میں ٹھیک ہوں", "ur")]
    [InlineData("আসসালামু আলাইকুম", "bn")]
    [InlineData("Hello world", null)]
    public void GuessesScript(string text, string? expected) => Assert.Equal(expected, Languages.GuessFromScript(text));

    [Theory]
    [InlineData("ar-EG", "ar")]
    [InlineData("pt-BR", "en")]
    [InlineData(null, "en")]
    public void NormalizesCodes(string? code, string expected) => Assert.Equal(expected, Languages.Normalize(code));
}

public class TurnTests
{
    [Fact]
    public void ParsesProviderShapes()
    {
        // Cloudflare: one server with a list of urls.
        var cf = IceServerProvider.Parse(JsonDocument.Parse("""{"urls":["turn:turn.cloudflare.com:3478?transport=udp","turns:turn.cloudflare.com:5349"],"username":"u","credential":"c"}""").RootElement);
        Assert.Single(cf);
        Assert.Equal(2, cf[0].Urls.Length);
        Assert.Equal("c", cf[0].Credential);

        // Metered: a list, each with a single url string.
        var m = IceServerProvider.Parse(JsonDocument.Parse("""[{"urls":"stun:stun.relay.metered.ca:80"},{"urls":"turn:global.relay.metered.ca:80","username":"a","credential":"b"}]""").RootElement);
        Assert.Equal(2, m.Count);
        Assert.Null(m[0].Username);
        Assert.Equal("turn:global.relay.metered.ca:80", m[1].Urls[0]);
    }
}

public class VisitorTests
{
    [Theory]
    [InlineData("/post/3f2b1c7e-1111-4a5b-9c9d-123456789abc?x=1", "/post/{id}")]
    [InlineData("/u/Aisha", "/u/…")]
    [InlineData("/", "/")]
    [InlineData("/Friends/", "/friends")]
    public void GroupsPages(string path, string expected) => Assert.Equal(expected, HalalBook.Api.Endpoints.VisitorEndpoints.NormalizePath(path));

    [Theory]
    [InlineData("https://www.google.com/", "Google")]
    [InlineData("https://l.facebook.com/l.php?u=x", "Facebook")]
    [InlineData("https://halalbook.app/welcome", "Direct")]
    [InlineData("", "Direct")]
    [InlineData("https://www.example.org/page", "example.org")]
    public void NamesSources(string referrer, string expected) => Assert.Equal(expected, HalalBook.Api.Endpoints.VisitorEndpoints.Source(referrer, "halalbook.app"));
}

public class NotificationRuleTests
{
    private static PushQueue.Item Msg(bool urgent = false, bool call = false) =>
        new(Guid.NewGuid(), urgent ? "🚨 Mum" : "Mum", "Come home for dinner", "/messages/x", "chat-x", true, Guid.NewGuid(), urgent, !call, call);

    [Fact]
    public void HidesTextWhenPreviewsAreOff() => Assert.Equal("New message", PushWorker.Plan(Msg(), false, false, false, true, false)!.Body);

    [Fact]
    public void DoNotDisturbHoldsAllButVips()
    {
        Assert.Null(PushWorker.Plan(Msg(), true, false, true, true, vip: false));
        Assert.NotNull(PushWorker.Plan(Msg(), true, false, true, true, vip: true));
        Assert.Null(PushWorker.Plan(Msg(), true, false, true, false, vip: true)); // VIPs not allowed through
    }

    [Fact]
    public void UrgentOnlyFromVipsAndNeverDisappears()
    {
        var fromVip = PushWorker.Plan(Msg(urgent: true), true, true, false, true, vip: true)!;
        Assert.True(fromVip.Urgent);
        Assert.False(fromVip.Disappear);
        var fromOther = PushWorker.Plan(Msg(urgent: true), true, true, false, true, vip: false)!;
        Assert.False(fromOther.Urgent);
        Assert.Equal("Mum", fromOther.Title);
        Assert.True(fromOther.Disappear);
    }
}

public class BirthdayTests
{
    [Theory]
    [InlineData("2000-02-29", "2027-02-27", 1)]   // leap-day birthdays fall on 28 February in other years
    [InlineData("1990-12-31", "2026-12-31", 0)]
    [InlineData("1990-01-01", "2026-12-31", 1)]
    public void CountsDaysToNextBirthday(string birthday, string today, int expected) =>
        Assert.Equal(expected, HalalBook.Api.Endpoints.UserEndpoints.DaysUntil(DateOnly.Parse(birthday), DateOnly.Parse(today)));
}

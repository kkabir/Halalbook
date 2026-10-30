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

using System.Globalization;
using HalalBook.Shared;

namespace HalalBook.Api.Services;

/// <summary>Astronomical prayer time calculation (standard solar-position formulas).</summary>
public static class PrayerTimeCalculator
{
    public record Method(string Key, string Name, double FajrAngle, double? IshaAngle, int? IshaMinutesAfterMaghrib);

    public static readonly IReadOnlyList<Method> Methods =
    [
        new("MWL", "Muslim World League", 18, 17, null),
        new("ISNA", "Islamic Society of North America", 15, 15, null),
        new("Egypt", "Egyptian General Authority of Survey", 19.5, 17.5, null),
        new("Makkah", "Umm al-Qura University, Makkah", 18.5, null, 90),
        new("Karachi", "University of Islamic Sciences, Karachi", 18, 18, null),
        new("Dubai", "Gulf Region (Dubai)", 18.2, 18.2, null),
        new("Singapore", "MUIS Singapore / JAKIM Malaysia", 20, 18, null),
        new("Turkey", "Diyanet, Turkey", 18, 17, null),
        new("Moonsighting", "Moonsighting Committee (approx.)", 18, 18, null),
    ];

    public static Method GetMethod(string? key) =>
        Methods.FirstOrDefault(m => string.Equals(m.Key, key, StringComparison.OrdinalIgnoreCase)) ?? Methods[0];

    public static PrayerTimesDto Calculate(DateOnly date, double lat, double lng, int utcOffsetMinutes, string? methodKey, string? asrSchool)
    {
        var method = GetMethod(methodKey);
        var hanafi = string.Equals(asrSchool, "Hanafi", StringComparison.OrdinalIgnoreCase);
        double tz = utcOffsetMinutes / 60.0;

        double jd = JulianDate(date) - lng / (15 * 24.0);
        var (decl, eqt) = SunPosition(jd + 0.5);

        double dhuhr = 12 + tz - lng / 15 - eqt;
        double sunrise = dhuhr - HourAngle(0.833, lat, decl);
        double sunset = dhuhr + HourAngle(0.833, lat, decl);
        double night = 24 - (sunset - sunrise);

        double fajr = dhuhr - HourAngle(method.FajrAngle, lat, decl);
        if (double.IsNaN(fajr)) fajr = sunrise - night * method.FajrAngle / 60; // high-latitude fallback (angle-based)

        double asrFactor = hanafi ? 2 : 1;
        double asrAngle = -Rad2Deg(Math.Atan(1 / (asrFactor + Math.Tan(Deg2Rad(Math.Abs(lat - decl))))));
        double asr = dhuhr + HourAngle(asrAngle, lat, decl);

        double maghrib = sunset;
        double isha;
        if (method.IshaMinutesAfterMaghrib is int mins) isha = maghrib + mins / 60.0;
        else
        {
            isha = dhuhr + HourAngle(method.IshaAngle!.Value, lat, decl);
            if (double.IsNaN(isha)) isha = sunset + night * method.IshaAngle.Value / 60;
        }

        var times = new (string Name, TimeOnly Time)[]
        {
            ("Fajr", ToTime(fajr)), ("Sunrise", ToTime(sunrise)), ("Dhuhr", ToTime(dhuhr + 1 / 60.0)),
            ("Asr", ToTime(asr)), ("Maghrib", ToTime(maghrib)), ("Isha", ToTime(isha)),
        };

        var nowLocal = TimeOnly.FromDateTime(DateTime.UtcNow.AddMinutes(utcOffsetMinutes));
        var next = times.Where(t => t.Name != "Sunrise").FirstOrDefault(t => t.Time > nowLocal);
        if (next.Name is null) next = times[0];

        return new PrayerTimesDto(date, method.Name, hanafi ? "Hanafi" : "Shafi", lat, lng, utcOffsetMinutes,
            times[0].Time, times[1].Time, times[2].Time, times[3].Time, times[4].Time, times[5].Time,
            next.Name, next.Time, HijriService.ToHijri(date));
    }

    // Hour angle (in hours) for the sun reaching `angle` degrees below the horizon.
    private static double HourAngle(double angle, double lat, double decl)
    {
        double num = -Math.Sin(Deg2Rad(angle)) - Math.Sin(Deg2Rad(decl)) * Math.Sin(Deg2Rad(lat));
        double den = Math.Cos(Deg2Rad(decl)) * Math.Cos(Deg2Rad(lat));
        double x = num / den;
        if (x < -1 || x > 1) return double.NaN;
        return Rad2Deg(Math.Acos(x)) / 15;
    }

    private static (double Declination, double EquationOfTime) SunPosition(double jd)
    {
        double d = jd - 2451545.0;
        double g = FixAngle(357.529 + 0.98560028 * d);
        double q = FixAngle(280.459 + 0.98564736 * d);
        double l = FixAngle(q + 1.915 * Math.Sin(Deg2Rad(g)) + 0.020 * Math.Sin(Deg2Rad(2 * g)));
        double e = 23.439 - 0.00000036 * d;
        double ra = Rad2Deg(Math.Atan2(Math.Cos(Deg2Rad(e)) * Math.Sin(Deg2Rad(l)), Math.Cos(Deg2Rad(l)))) / 15;
        double eqt = q / 15 - FixHour(ra);
        if (eqt > 12) eqt -= 24;
        if (eqt < -12) eqt += 24;
        double decl = Rad2Deg(Math.Asin(Math.Sin(Deg2Rad(e)) * Math.Sin(Deg2Rad(l))));
        return (decl, eqt);
    }

    private static double JulianDate(DateOnly date)
    {
        int y = date.Year, m = date.Month, day = date.Day;
        if (m <= 2) { y -= 1; m += 12; }
        double a = Math.Floor(y / 100.0);
        double b = 2 - a + Math.Floor(a / 4);
        return Math.Floor(365.25 * (y + 4716)) + Math.Floor(30.6001 * (m + 1)) + day + b - 1524.5;
    }

    private static TimeOnly ToTime(double hours)
    {
        if (double.IsNaN(hours)) return TimeOnly.MinValue;
        hours = FixHour(hours + 0.5 / 60); // round to nearest minute
        int h = (int)Math.Floor(hours);
        int min = (int)Math.Floor((hours - h) * 60);
        return new TimeOnly(h % 24, min);
    }

    private static double Deg2Rad(double d) => d * Math.PI / 180;
    private static double Rad2Deg(double r) => r * 180 / Math.PI;
    private static double FixAngle(double a) => a - 360 * Math.Floor(a / 360);
    private static double FixHour(double a) => a - 24 * Math.Floor(a / 24);
}

public static class QiblaCalculator
{
    public const double KaabaLat = 21.422487;
    public const double KaabaLng = 39.826206;

    public static QiblaDto Calculate(double lat, double lng)
    {
        double phi = ToRad(lat), lambda = ToRad(lng);
        double phiK = ToRad(KaabaLat), lambdaK = ToRad(KaabaLng);
        double y = Math.Sin(lambdaK - lambda);
        double x = Math.Cos(phi) * Math.Tan(phiK) - Math.Sin(phi) * Math.Cos(lambdaK - lambda);
        double bearing = (Math.Atan2(y, x) * 180 / Math.PI + 360) % 360;

        // Haversine distance
        double dPhi = phiK - phi, dLambda = lambdaK - lambda;
        double a = Math.Pow(Math.Sin(dPhi / 2), 2) + Math.Cos(phi) * Math.Cos(phiK) * Math.Pow(Math.Sin(dLambda / 2), 2);
        double dist = 6371.0 * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return new QiblaDto(lat, lng, Math.Round(bearing, 2), Math.Round(dist, 1));
    }

    private static double ToRad(double d) => d * Math.PI / 180;
}

public static class HijriService
{
    private static readonly Calendar UmAlQura = new UmAlQuraCalendar();
    private static readonly Calendar Tabular = new HijriCalendar();

    public static readonly string[] MonthNames =
        ["Muharram", "Safar", "Rabi al-Awwal", "Rabi al-Thani", "Jumada al-Ula", "Jumada al-Akhirah",
         "Rajab", "Sha'ban", "Ramadan", "Shawwal", "Dhu al-Qa'dah", "Dhu al-Hijjah"];

    public static readonly string[] MonthNamesArabic =
        ["مُحَرَّم", "صَفَر", "رَبِيع الأَوَّل", "رَبِيع الثَّانِي", "جُمَادَى الأُولَى", "جُمَادَى الآخِرَة",
         "رَجَب", "شَعْبَان", "رَمَضَان", "شَوَّال", "ذُو القَعْدَة", "ذُو الحِجَّة"];

    private static readonly (int Month, int Day, string Name)[] Occasions =
    [
        (1, 1, "Islamic New Year"),
        (1, 10, "Day of Ashura"),
        (3, 12, "Mawlid an-Nabi (observed by many)"),
        (7, 27, "Isra and Mi'raj"),
        (8, 15, "Mid-Sha'ban"),
        (9, 1, "First day of Ramadan"),
        (9, 27, "Laylat al-Qadr (27th night)"),
        (10, 1, "Eid al-Fitr"),
        (12, 8, "Start of Hajj"),
        (12, 9, "Day of Arafah"),
        (12, 10, "Eid al-Adha"),
    ];

    private static Calendar CalendarFor(DateTime dt) =>
        dt >= UmAlQura.MinSupportedDateTime && dt <= UmAlQura.MaxSupportedDateTime ? UmAlQura : Tabular;

    public static HijriDateDto ToHijri(DateOnly date, int adjustDays = 0)
    {
        var dt = date.ToDateTime(TimeOnly.MinValue).AddDays(adjustDays);
        var cal = CalendarFor(dt);
        int y = cal.GetYear(dt), m = cal.GetMonth(dt), d = cal.GetDayOfMonth(dt);
        return new HijriDateDto(y, m, d, MonthNames[m - 1], MonthNamesArabic[m - 1], $"{d} {MonthNames[m - 1]} {y} AH");
    }

    public static DateOnly ToGregorian(int hYear, int hMonth, int hDay)
    {
        try { return DateOnly.FromDateTime(UmAlQura.ToDateTime(hYear, hMonth, hDay, 0, 0, 0, 0)); }
        catch (ArgumentOutOfRangeException) { return DateOnly.FromDateTime(Tabular.ToDateTime(hYear, hMonth, hDay, 0, 0, 0, 0)); }
    }

    public static List<string> OccasionsFor(HijriDateDto h)
    {
        var list = Occasions.Where(o => o.Month == h.Month && o.Day == h.Day).Select(o => o.Name).ToList();
        if (h.Day is >= 13 and <= 15 && h.Month != 9) list.Add("Ayyam al-Bid (white days fasting)");
        return list;
    }

    public static List<CalendarDayDto> Month(int year, int month)
    {
        var days = new List<CalendarDayDto>();
        int count = DateTime.DaysInMonth(year, month);
        for (int i = 1; i <= count; i++)
        {
            var date = new DateOnly(year, month, i);
            var h = ToHijri(date);
            days.Add(new CalendarDayDto(date, h, OccasionsFor(h)));
        }
        return days;
    }

    public static List<IslamicOccasionDto> Upcoming(DateOnly from, int count = 8)
    {
        var start = ToHijri(from);
        var result = new List<IslamicOccasionDto>();
        for (int year = start.Year; year <= start.Year + 2 && result.Count < count; year++)
        {
            foreach (var o in Occasions)
            {
                var g = ToGregorian(year, o.Month, o.Day);
                if (g < from) continue;
                result.Add(new IslamicOccasionDto(o.Name, ToHijri(g), g));
                if (result.Count >= count) break;
            }
        }
        return result.OrderBy(r => r.GregorianDate).ToList();
    }
}

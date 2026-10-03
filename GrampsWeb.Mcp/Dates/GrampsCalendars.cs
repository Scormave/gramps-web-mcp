namespace GrampsWeb.Mcp.Dates;

/// <summary>
/// Ports of the calendar conversions in Gramps <c>gramps/gen/lib/gcalendar.py</c>: a date in each Gramps calendar
/// to the serial day number (SDN) Gramps sorts dates by, and back. Gramps checks a date by converting it to the SDN
/// and back to the same calendar.
/// </summary>
/// <remarks>
/// Python floors integer division and takes the sign of the divisor for <c>%</c>, so the ports use
/// <see cref="FloorDiv"/> and <see cref="FloorMod"/> where Gramps uses <c>//</c> and <c>%</c>. The Persian and Islamic
/// conversions add half days in floating point; the ports do the same arithmetic in integers, which gives the same
/// results for whole-day SDNs.
/// </remarks>
internal static class GrampsCalendars
{
    public const int Gregorian = 0;
    public const int Julian = 1;
    public const int Hebrew = 2;
    public const int FrenchRepublican = 3;
    public const int Persian = 4;
    public const int Islamic = 5;
    public const int Swedish = 6;

    /// <summary>Calendar names by code, as Gramps <c>Date.calendar_names</c> spells them.</summary>
    public static readonly IReadOnlyList<string> Names =
        ["Gregorian", "Julian", "Hebrew", "French Republican", "Persian", "Islamic", "Swedish"];

    public static bool IsKnown(int calendar) => calendar is >= Gregorian and <= Swedish;

    /// <summary>
    /// Gramps <c>calendar_has_fixed_newyear</c>: only Gregorian, Julian, and Swedish years may start on another day
    /// than the calendar's own new year.
    /// </summary>
    public static bool HasFixedNewYear(int calendar) => calendar is not (Gregorian or Julian or Swedish);

    /// <summary>Months in a year: the Hebrew leap month and the French complementary days make 13.</summary>
    public static int MonthsInYear(int calendar) => calendar is Hebrew or FrenchRepublican ? 13 : 12;

    /// <summary>True for the calendars whose months are the Julian months with English names.</summary>
    public static bool HasEnglishMonthNames(int calendar) => calendar is Gregorian or Julian or Swedish;

    /// <summary>Gramps <c>Date._calendar_convert</c>: the SDN of a date, which must have a year, month, and day.</summary>
    public static int ToSdn(int calendar, int year, int month, int day) => calendar switch
    {
        Gregorian => GregorianSdn(year, month, day),
        Julian => JulianSdn(year, month, day),
        Hebrew => HebrewSdn(year, month, day),
        FrenchRepublican => FrenchSdn(year, month, day),
        Persian => PersianSdn(year, month, day),
        Islamic => IslamicSdn(year, month, day),
        Swedish => SwedishSdn(year, month, day),
        _ => throw new ArgumentOutOfRangeException(nameof(calendar), calendar, "Unknown Gramps calendar.")
    };

    /// <summary>Gramps <c>Date._calendar_change</c>: the date of an SDN in a calendar.</summary>
    public static (int Year, int Month, int Day) FromSdn(int calendar, int sdn) => calendar switch
    {
        Gregorian => GregorianYmd(sdn),
        Julian => JulianYmd(sdn),
        Hebrew => HebrewYmd(sdn),
        FrenchRepublican => FrenchYmd(sdn),
        Persian => PersianYmd(sdn),
        Islamic => IslamicYmd(sdn),
        Swedish => SwedishYmd(sdn),
        _ => throw new ArgumentOutOfRangeException(nameof(calendar), calendar, "Unknown Gramps calendar.")
    };

    // ── Gregorian ──

    private const int GrgSdnOffset = 32045;
    private const int GrgDaysPer5Months = 153;
    private const int GrgDaysPer4Years = 1461;
    private const int GrgDaysPer400Years = 146097;

    private static int GregorianSdn(int year, int month, int day)
    {
        year += year < 0 ? 4801 : 4800;
        if (month > 2)
            month -= 3;
        else
        {
            month += 9;
            year -= 1;
        }

        return FloorDiv(FloorDiv(year, 100) * GrgDaysPer400Years, 4)
            + FloorDiv(FloorMod(year, 100) * GrgDaysPer4Years, 4)
            + FloorDiv(month * GrgDaysPer5Months + 2, 5)
            + day
            - GrgSdnOffset;
    }

    private static (int, int, int) GregorianYmd(int sdn)
    {
        var temp = (GrgSdnOffset + sdn) * 4 - 1;
        var century = FloorDiv(temp, GrgDaysPer400Years);
        temp = FloorDiv(FloorMod(temp, GrgDaysPer400Years), 4) * 4 + 3;
        var year = century * 100 + FloorDiv(temp, GrgDaysPer4Years);
        var dayOfYear = FloorDiv(FloorMod(temp, GrgDaysPer4Years), 4) + 1;

        temp = dayOfYear * 5 - 3;
        var month = FloorDiv(temp, GrgDaysPer5Months);
        var day = FloorDiv(FloorMod(temp, GrgDaysPer5Months), 5) + 1;
        return NormalizeMarchYear(year, month, day);
    }

    // ── Julian ──

    private const int JlnSdnOffset = 32083;
    private const int JlnDaysPer5Months = 153;
    private const int JlnDaysPer4Years = 1461;

    private static int JulianSdn(int year, int month, int day)
    {
        year += year < 0 ? 4801 : 4800;
        if (month > 2)
            month -= 3;
        else
        {
            month += 9;
            year -= 1;
        }

        return FloorDiv(year * JlnDaysPer4Years, 4)
            + FloorDiv(month * JlnDaysPer5Months + 2, 5)
            + day
            - JlnSdnOffset;
    }

    private static (int, int, int) JulianYmd(int sdn)
    {
        var temp = (sdn + JlnSdnOffset) * 4 - 1;
        var year = FloorDiv(temp, JlnDaysPer4Years);
        var dayOfYear = FloorDiv(FloorMod(temp, JlnDaysPer4Years), 4) + 1;

        temp = dayOfYear * 5 - 3;
        var month = FloorDiv(temp, JlnDaysPer5Months);
        var day = FloorDiv(FloorMod(temp, JlnDaysPer5Months), 5) + 1;
        return NormalizeMarchYear(year, month, day);
    }

    /// <summary>The Gregorian and Julian conversions count from March of year -4800; back to January and B.C.E.</summary>
    private static (int, int, int) NormalizeMarchYear(int year, int month, int day)
    {
        if (month < 10)
            month += 3;
        else
        {
            year += 1;
            month -= 9;
        }

        year -= 4800;
        if (year <= 0)
            year -= 1;
        return (year, month, day);
    }

    // ── Swedish: Julian with one day less from March 1700 to the extra 30 February 1712, Gregorian from March 1753 ──

    private static int SwedishSdn(int year, int month, int day)
    {
        var date = (year, month, day);
        if (date.CompareTo((1700, 3, 1)) >= 0 && date.CompareTo((1712, 2, 30)) <= 0)
            return JulianSdn(year, month, day) - 1;
        if (date.CompareTo((1753, 3, 1)) >= 0)
            return GregorianSdn(year, month, day);
        return JulianSdn(year, month, day);
    }

    private static (int, int, int) SwedishYmd(int sdn)
    {
        if (sdn == 2346425)
            return (1712, 2, 30);
        if (sdn is >= 2342042 and < 2346425)
            return JulianYmd(sdn + 1);
        if (sdn >= 2361390)
            return GregorianYmd(sdn);
        return JulianYmd(sdn);
    }

    // ── French Republican ──

    private const int FrSdnOffset = 2375474;
    private const int FrDaysPer4Years = 1461;
    private const int FrDaysPerMonth = 30;

    private static int FrenchSdn(int year, int month, int day) =>
        FloorDiv(year * FrDaysPer4Years, 4) + (month - 1) * FrDaysPerMonth + day + FrSdnOffset;

    private static (int, int, int) FrenchYmd(int sdn)
    {
        var temp = (sdn - FrSdnOffset) * 4 - 1;
        var year = FloorDiv(temp, FrDaysPer4Years);
        var dayOfYear = FloorDiv(FloorMod(temp, FrDaysPer4Years), 4);
        var month = FloorDiv(dayOfYear, FrDaysPerMonth) + 1;
        var day = FloorMod(dayOfYear, FrDaysPerMonth) + 1;
        return (year, month, day);
    }

    // ── Persian ──

    /// <summary><c>_PRS_EPOCH - 1 = 1948319.5</c>; <c>ceil(n + 1948319.5) = n + 1948320</c> for whole <c>n</c>.</summary>
    private const int PrsEpochCeil = 1948320;

    private static int PersianSdn(int year, int month, int day)
    {
        var epbase = year >= 0 ? year - 474 : year - 473;
        var epyear = 474 + FloorMod(epbase, 2820);
        var v1 = month <= 7 ? (month - 1) * 31 : (month - 1) * 30 + 6;
        var v2 = FloorDiv(epyear * 682 - 110, 2816);
        var v3 = (epyear - 1) * 365 + day;
        var v4 = FloorDiv(epbase, 2820) * 1029983;
        return v1 + v2 + v3 + v4 + PrsEpochCeil;
    }

    private static (int, int, int) PersianYmd(int sdn)
    {
        var depoch = sdn - 2121446;
        var cycle = FloorDiv(depoch, 1029983);
        var cyear = FloorMod(depoch, 1029983);
        int ycycle;
        if (cyear == 1029982)
            ycycle = 2820;
        else
        {
            var aux1 = FloorDiv(cyear, 366);
            var aux2 = FloorMod(cyear, 366);
            ycycle = FloorDiv(2134 * aux1 + 2816 * aux2 + 2815, 1028522) + aux1 + 1;
        }

        var year = ycycle + 2820 * cycle + 474;
        if (year <= 0)
            year -= 1;

        var yday = sdn - PersianSdn(year, 1, 1) + 1;
        var month = yday < 186 ? CeilDiv(yday, 31) : CeilDiv(yday - 6, 30);
        var day = sdn - PersianSdn(year, month, 1) + 1;
        return (year, month, day);
    }

    // ── Islamic ──

    /// <summary><c>_ISM_EPOCH - 1 = 1948438.5</c>; <c>ceil(n + 1948438.5) = n + 1948439</c> for whole <c>n</c>.</summary>
    private const int IsmEpochCeil = 1948439;

    private static int IslamicSdn(int year, int month, int day)
    {
        var v1 = CeilDiv(59 * (month - 1), 2); // ceil(29.5 * (month - 1))
        var v2 = (year - 1) * 354;
        var v3 = FloorDiv(3 + 11 * year, 30);
        return day + v1 + v2 + v3 + IsmEpochCeil;
    }

    private static (int, int, int) IslamicYmd(int sdn)
    {
        // Gramps works on sdn + 0.5: floor((30 * (sdn + 0.5 - 1948439.5) + 10646) / 10631).
        var year = FloorDiv(30 * (sdn - IsmEpochCeil) + 10646, 10631);
        // ceil((sdn + 0.5 - (29 + start)) / 29.5) + 1, at most 12.
        var month = Math.Min(12, CeilDiv(2 * (sdn - 29 - IslamicSdn(year, 1, 1)) + 1, 59) + 1);
        // int(sdn + 0.5 - first + 1) truncates toward zero.
        var day = sdn - IslamicSdn(year, month, 1) + 1;
        if (day < 0)
            day += 1;
        return (year, month, day);
    }

    // ── Hebrew ──

    private const long HbrHalakimPerHour = 1080;
    private const long HbrHalakimPerDay = 25920;
    private const long HbrHalakimPerLunarCycle = 29 * HbrHalakimPerDay + 13753;
    private const long HbrHalakimPerMetonicCycle = HbrHalakimPerLunarCycle * (12 * 19 + 7);
    private const int HbrSdnOffset = 347997;
    private const long HbrNewMoonOfCreation = 31524;
    private const long HbrNoon = 18 * HbrHalakimPerHour;
    private const long HbrAm3_11_20 = 9 * HbrHalakimPerHour + 204;
    private const long HbrAm9_32_43 = 15 * HbrHalakimPerHour + 589;

    private const long HbrSunday = 0;
    private const long HbrMonday = 1;
    private const long HbrTuesday = 2;
    private const long HbrWednesday = 3;
    private const long HbrFriday = 5;

    private static readonly int[] HbrMonthsPerYear = [12, 12, 13, 12, 12, 13, 12, 13, 12, 12, 13, 12, 12, 13, 12, 12, 13, 12, 13];
    private static readonly int[] HbrYearOffset = [0, 12, 24, 37, 49, 61, 74, 86, 99, 111, 123, 136, 148, 160, 173, 185, 197, 210, 222];

    private static long Tishri1(long metonicYear, long moladDay, long moladHalakim)
    {
        var tishri1 = moladDay;
        var dow = FloorMod(tishri1, 7);
        var leapYear = metonicYear is 2 or 5 or 7 or 10 or 13 or 16 or 18;
        var lastWasLeapYear = metonicYear is 3 or 6 or 8 or 11 or 14 or 17 or 0;

        // Rules 2, 3 and 4.
        if (moladHalakim >= HbrNoon
            || (!leapYear && dow == HbrTuesday && moladHalakim >= HbrAm3_11_20)
            || (lastWasLeapYear && dow == HbrMonday && moladHalakim >= HbrAm9_32_43))
        {
            tishri1 += 1;
            dow += 1;
            if (dow == 7)
                dow = 0;
        }

        // Rule 1 last, because it can delay one more day.
        if (dow == HbrWednesday || dow == HbrFriday || dow == HbrSunday)
            tishri1 += 1;

        return tishri1;
    }

    private static (long Cycle, long Year, long Day, long Halakim) TishriMolad(long inputDay)
    {
        // An estimate that may be low, never high; the loop corrects it.
        var metonicCycle = FloorDiv(inputDay + 310, 6940);
        var (moladDay, moladHalakim) = MoladOfMetonicCycle(metonicCycle);

        while (moladDay < inputDay - 6940 + 310)
        {
            metonicCycle += 1;
            moladHalakim += HbrHalakimPerMetonicCycle;
            moladDay += FloorDiv(moladHalakim, HbrHalakimPerDay);
            moladHalakim = FloorMod(moladHalakim, HbrHalakimPerDay);
        }

        // The molad of Tishri closest to the date. Python's loop variable stops at 19 when the loop runs out.
        long metonicYear = 0;
        for (var y = 0; y < 20; y++)
        {
            metonicYear = y;
            if (moladDay > inputDay - 74)
                break;

            moladHalakim += HbrHalakimPerLunarCycle * HbrMonthsPerYear[y];
            moladDay += FloorDiv(moladHalakim, HbrHalakimPerDay);
            moladHalakim = FloorMod(moladHalakim, HbrHalakimPerDay);
        }

        return (metonicCycle, metonicYear, moladDay, moladHalakim);
    }

    private static (long Day, long Halakim) MoladOfMetonicCycle(long metonicCycle)
    {
        // metonic_cycle * HALAKIM_PER_METONIC_CYCLE in 16-bit halves, as Gramps does.
        var r1 = HbrNewMoonOfCreation + metonicCycle * (HbrHalakimPerMetonicCycle & 0xFFFF);
        var r2 = r1 >> 16;
        r2 += metonicCycle * ((HbrHalakimPerMetonicCycle >> 16) & 0xFFFF);

        var d2 = FloorDiv(r2, HbrHalakimPerDay);
        r2 -= d2 * HbrHalakimPerDay;
        r1 = (r2 << 16) | (r1 & 0xFFFF);
        var d1 = FloorDiv(r1, HbrHalakimPerDay);
        r1 -= d1 * HbrHalakimPerDay;

        return ((d2 << 16) | d1, r1);
    }

    private static (long Cycle, long Year, long MoladDay, long MoladHalakim, long Tishri1) StartOfYear(long year)
    {
        var metonicCycle = FloorDiv(year - 1, 19);
        var metonicYear = FloorMod(year - 1, 19);
        var (moladDay, moladHalakim) = MoladOfMetonicCycle(metonicCycle);

        moladHalakim += HbrHalakimPerLunarCycle * HbrYearOffset[metonicYear];
        moladDay += FloorDiv(moladHalakim, HbrHalakimPerDay);
        moladHalakim = FloorMod(moladHalakim, HbrHalakimPerDay);

        return (metonicCycle, metonicYear, moladDay, moladHalakim, Tishri1(metonicYear, moladDay, moladHalakim));
    }

    private static int HebrewSdn(int year, int month, int day)
    {
        long sdn;
        if (month is 1 or 2)
        {
            // Tishri or Heshvan: the year length is not needed.
            var tishri1 = StartOfYear(year).Tishri1;
            sdn = month == 1 ? tishri1 + day - 1 : tishri1 + day + 29;
        }
        else if (month == 3)
        {
            // Kislev: Heshvan has 30 days in a 355- or 385-day year.
            var (_, metonicYear, moladDay, moladHalakim, tishri1) = StartOfYear(year);
            moladHalakim += HbrHalakimPerLunarCycle * HbrMonthsPerYear[metonicYear];
            moladDay += FloorDiv(moladHalakim, HbrHalakimPerDay);
            moladHalakim = FloorMod(moladHalakim, HbrHalakimPerDay);
            var tishri1After = Tishri1(FloorMod(metonicYear + 1, 19), moladDay, moladHalakim);

            var yearLength = tishri1After - tishri1;
            sdn = yearLength is 355 or 385 ? tishri1 + day + 59 : tishri1 + day + 58;
        }
        else if (month is 4 or 5 or 6)
        {
            // Tevet, Shevat, or Adar I: counted back from the next Tishri.
            var tishri1After = StartOfYear(year + 1L).Tishri1;
            var lengthOfAdar1And2 = HbrMonthsPerYear[FloorMod(year - 1L, 19)] == 12 ? 29 : 59;
            sdn = month switch
            {
                4 => tishri1After + day - lengthOfAdar1And2 - 237,
                5 => tishri1After + day - lengthOfAdar1And2 - 208,
                _ => tishri1After + day - lengthOfAdar1And2 - 178
            };
        }
        else
        {
            // Adar II or later: counted back from the next Tishri.
            var tishri1After = StartOfYear(year + 1L).Tishri1;
            switch (month)
            {
                case 7: sdn = tishri1After + day - 207; break;
                case 8: sdn = tishri1After + day - 178; break;
                case 9: sdn = tishri1After + day - 148; break;
                case 10: sdn = tishri1After + day - 119; break;
                case 11: sdn = tishri1After + day - 89; break;
                case 12: sdn = tishri1After + day - 60; break;
                case 13: sdn = tishri1After + day - 30; break;
                default: return 0;
            }
        }

        return (int)(sdn + HbrSdnOffset);
    }

    private static (int, int, int) HebrewYmd(int sdn)
    {
        long inputDay = sdn - HbrSdnOffset;
        var (metonicCycle, metonicYear, day1, halakim) = TishriMolad(inputDay);
        var tishri1 = Tishri1(metonicYear, day1, halakim);
        long year, tishri1After;

        if (inputDay >= tishri1)
        {
            // Tishri 1 found at the start of the year.
            year = metonicCycle * 19 + metonicYear + 1;
            if (inputDay < tishri1 + 59)
            {
                return inputDay < tishri1 + 30
                    ? ((int)year, 1, (int)(inputDay - tishri1 + 1))
                    : ((int)year, 2, (int)(inputDay - tishri1 - 29));
            }

            // The year length decides; find Tishri 1 of the next year.
            halakim += HbrHalakimPerLunarCycle * HbrMonthsPerYear[metonicYear];
            day1 += FloorDiv(halakim, HbrHalakimPerDay);
            halakim = FloorMod(halakim, HbrHalakimPerDay);
            tishri1After = Tishri1(FloorMod(metonicYear + 1, 19), day1, halakim);
        }
        else
        {
            // Tishri 1 found at the end of the year.
            year = metonicCycle * 19 + metonicYear;
            if (inputDay >= tishri1 - 177)
            {
                // One of the last six months.
                if (inputDay > tishri1 - 30)
                    return ((int)year, 13, (int)(inputDay - tishri1 + 30));
                if (inputDay > tishri1 - 60)
                    return ((int)year, 12, (int)(inputDay - tishri1 + 60));
                if (inputDay > tishri1 - 89)
                    return ((int)year, 11, (int)(inputDay - tishri1 + 89));
                if (inputDay > tishri1 - 119)
                    return ((int)year, 10, (int)(inputDay - tishri1 + 119));
                if (inputDay > tishri1 - 148)
                    return ((int)year, 9, (int)(inputDay - tishri1 + 148));
                return ((int)year, 8, (int)(inputDay - tishri1 + 178));
            }

            long month;
            long day;
            if (HbrMonthsPerYear[FloorMod(year - 1, 19)] == 13)
            {
                month = 7;
                day = inputDay - tishri1 + 207;
                if (day > 0)
                    return ((int)year, (int)month, (int)day);
                month -= 1;
                day += 30;
                if (day > 0)
                    return ((int)year, (int)month, (int)day);
                month -= 1;
                day += 30;
            }
            else
            {
                month = 6;
                day = inputDay - tishri1 + 207;
                if (day > 0)
                    return ((int)year, (int)month, (int)day);
                month -= 1;
                day += 30;
            }

            if (day > 0)
                return ((int)year, (int)month, (int)day);
            month -= 1;
            day += 29;
            if (day > 0)
                return ((int)year, (int)month, (int)day);

            // The year length decides; find Tishri 1 of this year.
            tishri1After = tishri1;
            (_, metonicYear, day1, halakim) = TishriMolad(day1 - 365);
            tishri1 = Tishri1(metonicYear, day1, halakim);
        }

        var yearLength = tishri1After - tishri1;
        var dayOfHeshvan = inputDay - tishri1 - 29;
        if (yearLength is 355 or 385)
        {
            // Heshvan has 30 days.
            if (dayOfHeshvan <= 30)
                return ((int)year, 2, (int)dayOfHeshvan);
            dayOfHeshvan -= 30;
        }
        else
        {
            // Heshvan has 29 days.
            if (dayOfHeshvan <= 29)
                return ((int)year, 2, (int)dayOfHeshvan);
            dayOfHeshvan -= 29;
        }

        // Kislev.
        return ((int)year, 3, (int)dayOfHeshvan);
    }

    // ── Python integer division ──

    private static int FloorDiv(int a, int b) => (int)FloorDiv((long)a, b);

    private static long FloorDiv(long a, long b)
    {
        var q = a / b;
        return (a % b != 0 && (a < 0) != (b < 0)) ? q - 1 : q;
    }

    private static int FloorMod(int a, int b) => (int)FloorMod((long)a, b);

    private static long FloorMod(long a, long b)
    {
        var r = a % b;
        return r != 0 && (r < 0) != (b < 0) ? r + b : r;
    }

    private static int CeilDiv(int a, int b) => -FloorDiv(-a, b);
}

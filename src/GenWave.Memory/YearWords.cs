using System.Globalization;

namespace GenWave.Memory;

/// <summary>
/// The tokens a spoken year can come out as (SPEC F218.11): the digits, plus the number words for 1900-2099
/// ("nineteen fifty nine", "two thousand ten", "twenty twenty", "nineteen fifties"). Outside that range, digits only. Pure.
/// This is only the vocabulary: a number word counts as a year fact when <see cref="ExcludedWords.YearFacts"/> sees it
/// beside another year token, since "one" and "ten" are also ordinary words.
/// </summary>
static class YearWords
{
    const int FirstSpokenYear = 1900;
    const int LastSpokenYear = 2099;

    static readonly string[] Units =
        ["", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine"];

    static readonly string[] Teens =
        ["ten", "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen"];

    static readonly string[] Tens =
        ["", "", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety"];

    static readonly string[] Decades =
        ["", "", "twenties", "thirties", "forties", "fifties", "sixties", "seventies", "eighties", "nineties"];

    public static IReadOnlySet<string> For(int? year)
    {
        var words = new HashSet<string>(StringComparer.Ordinal);
        if (year is not { } y || y <= 0)
            return words;

        words.Add(y.ToString(CultureInfo.InvariantCulture));
        if (y < FirstSpokenYear || y > LastSpokenYear)
            return words;

        var rest = y % 100;
        words.Add(rest.ToString(CultureInfo.InvariantCulture));
        if (rest < 10)
            words.Add(rest.ToString("00", CultureInfo.InvariantCulture));
        if (y < 2000)
            words.Add("nineteen");
        else
            words.UnionWith(["two", "thousand", "twenty"]);

        if (rest == 0)
            words.UnionWith(["hundred", "hundreds", "oh"]);
        else if (rest < 10)
            words.UnionWith(["oh", Units[rest]]);
        else if (rest < 20)
            words.Add(Teens[rest - 10]);
        else
        {
            words.Add(Tens[rest / 10]);
            words.Add(Decades[rest / 10]);
            if (rest % 10 != 0)
                words.Add(Units[rest % 10]);
        }
        return words;
    }
}

using System.Globalization;
using System.Text.RegularExpressions;
using PlantCad.Core.Import;

namespace PlantCad.Core.Data;

public static class Parsing
{
    private static readonly Regex RangeMeters = new(@"(?<min>[0-9]+(?:\.[0-9]+)?)\s*-\s*(?<max>[0-9]+(?:\.[0-9]+)?)\s*m", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex RangeNumbers = new(@"(?<min>[0-9]+(?:\.[0-9]+)?)\s*-\s*(?<max>[0-9]+(?:\.[0-9]+)?)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ContainerCode = new(@"C\s*(?<num>[0-9]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Dictionary<string, int> RomanToMonth = new(StringComparer.OrdinalIgnoreCase)
    {
        {"I",1},{"II",2},{"III",3},{"IV",4},{"V",5},{"VI",6},{"VII",7},{"VIII",8},{"IX",9},{"X",10},{"XI",11},{"XII",12}
    };

    public static (double? min, double? max) ParseRangeMeters(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return (null, null);
        var t = text.Replace(',', '.');
        var m = RangeMeters.Match(t);
        if (m.Success)
        {
            return (ToDouble(m.Groups["min"].Value), ToDouble(m.Groups["max"].Value));
        }
        // may be like "8-10 m" without unit after each
        var m2 = RangeNumbers.Match(t);
        if (m2.Success && (t.IndexOf('m') >= 0 || t.IndexOf('M') >= 0))
        {
            return (ToDouble(m2.Groups["min"].Value), ToDouble(m2.Groups["max"].Value));
        }
        return (null, null);
    }

    public static (double? min, double? max) ParseRangeMetersOrSingle(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return (null, null);
        var (min, max) = ParseRangeMeters(text);
        if (min.HasValue || max.HasValue) return (min, max);
        // single like "3 m"
        var t = text.Replace(',', '.');
        var parts = t.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length > 0 && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            return (v, v);
        return (null, null);
    }

    public static (int? start, int? end) ParseFloweringMonths(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return (null, null);
        var t = text.Trim().ToUpperInvariant();
        if (t.Contains('-'))
        {
            var parts = t.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && RomanToMonth.TryGetValue(parts[0], out var s) && RomanToMonth.TryGetValue(parts[1], out var e))
                return (s, e);
        }
        if (RomanToMonth.TryGetValue(t, out var m))
            return (m, m);
        return (null, null);
    }

    public static (int? zone, string? subzone) ParseHardiness(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return (null, null);
        var t = text.Trim();
        // examples: 5b, 4, 3
        var numPart = new string(t.TakeWhile(char.IsDigit).ToArray());
        if (int.TryParse(numPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out var zone))
        {
            var rest = t[numPart.Length..].Trim();
            var sub = string.IsNullOrWhiteSpace(rest) ? null : rest;
            return (zone, sub);
        }
        return (null, null);
    }

    public static (double? min, double? max, int? minOrdinal, int? maxOrdinal) ParseMoisture(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return (null, null, null, null);
        var t = text.Replace("do", "-").Replace(',', '.');
        var tokens = t.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 2)
        {
            var a = MapMoisture(tokens[0]);
            var b = MapMoisture(tokens[1]);
            return (null, null, Math.Min(a, b), Math.Max(a, b));
        }
        var single = MapMoisture(t);
        return (null, null, single, single);
    }

    private static int MapMoisture(string token)
    {
        var key = PolishTermMaps.Normalize(token);
        return PolishTermMaps.MoistureOrdinal.TryGetValue(key, out var ord) ? ord : 2; // default moderate
    }

    public static (double? min, double? max, string? minClass, string? maxClass) ParsePh(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return (null, null, null, null);
        var t = text.Trim();
        // numeric like 6.5-7.5 or 5.5 - 7.0
        var m = RangeNumbers.Match(t.Replace(',', '.'));
        if (m.Success)
        {
            return (ToDouble(m.Groups["min"].Value), ToDouble(m.Groups["max"].Value), null, null);
        }
        // textual classes
        var key = PolishTermMaps.Normalize(t);
        if (PolishTermMaps.PhClassRange.TryGetValue(key, out var range))
            return (null, null, range.minCode, range.maxCode);
        return (null, null, null, null);
    }

    public static (double? min, double? max) ParseSpacingMeters(string? text)
        => ParseRangeMetersOrSingle(text);

    public static (string genus, string? species, string? cultivar, string display) ParseBotanical(string? botanicalName, string? name)
    {
        var display = (botanicalName ?? name ?? string.Empty).Trim();
        var text = (botanicalName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            // fallback: sometimes cultivar is only in Name
            text = (name ?? string.Empty).Trim();
        }
        // cultivar may be in single quotes
        string? cultivar = null;
        var idx1 = text.IndexOf('\'');
        var idx2 = text.LastIndexOf('\'');
        if (idx1 >= 0 && idx2 > idx1)
        {
            cultivar = text.Substring(idx1 + 1, idx2 - idx1 - 1).Trim();
            text = (text.Remove(idx1, idx2 - idx1 + 1)).Trim();
        }
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var genus = parts.Length > 0 ? parts[0] : display;
        string? species = parts.Length > 1 ? parts[1] : null;
        return (genus, species, cultivar, display);
    }

    public static (int? start, int? end) ParseFloweringRange(string? text)
        => ParseFloweringMonths(text);

    public static IEnumerable<string> SplitTokens(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) yield break;
        foreach (var tok in text.Split(new[] { ',', '/', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var trimmed = tok.Trim();
            if (trimmed.Length == 0) continue;
            yield return trimmed;
        }
    }

    public static double? ToDouble(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        if (double.TryParse(s.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            return v;
        return null;
    }
}

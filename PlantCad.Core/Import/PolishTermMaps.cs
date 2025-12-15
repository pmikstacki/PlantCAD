using System.Globalization;
using System.Text;

namespace PlantCad.Core.Import;

public static class PolishTermMaps
{
    public static readonly Dictionary<string, string> Exposure = new(StringComparer.OrdinalIgnoreCase)
    {
        { Normalize("Słoneczne"), "full_sun" },
        { Normalize("Sloneczne"), "full_sun" },
        { Normalize("Półcień"), "partial_shade" },
        { Normalize("Polcień"), "partial_shade" },
        { Normalize("Cień"), "shade" },
        { Normalize("Cien"), "shade" },
    };

    public static readonly Dictionary<string, string> Habit = new(StringComparer.OrdinalIgnoreCase)
    {
        { Normalize("Kolumnowy"), "columnar" },
        { Normalize("Bardzo wąski, kolumnowy"), "narrow_columnar" },
        { Normalize("Wąskokolumnowy"), "narrow_columnar" },
        { Normalize("Wąski, kolumnowy"), "narrow_columnar" },
        { Normalize("Płaczący"), "weeping" },
        { Normalize("Parasolowaty"), "umbrella" },
        { Normalize("Stożkowaty"), "conical" },
        { Normalize("Szeroki, rozłożysty"), "broad" },
        { Normalize("Owalny"), "oval" },
        { Normalize("Nieregularny"), "irregular" },
        { Normalize("Luźny"), "loose" },
        { Normalize("Wielopniowy"), "multi_stem" },
    };

    public static readonly Dictionary<string, int> MoistureOrdinal = new(StringComparer.OrdinalIgnoreCase)
    {
        { Normalize("Sucha"), 1 },
        { Normalize("Niska"), 1 },
        { Normalize("Umiarkowana"), 2 },
        { Normalize("Umiarkowana do suchej"), 1 },
        { Normalize("Umiarkowana do wilgotnej"), 2 },
        { Normalize("Wilgotna"), 3 },
        { Normalize("Mokra"), 4 },
    };

    public static readonly Dictionary<string, (string minCode, string maxCode)> PhClassRange = new(StringComparer.OrdinalIgnoreCase)
    {
        { Normalize("Kwaśny"), ("acidic","acidic") },
        { Normalize("Kwaśny do obojętnego"), ("acidic","neutral") },
        { Normalize("Obojętny"), ("neutral","neutral") },
        { Normalize("Obojętny do kwaśnego"), ("neutral","acidic") },
        { Normalize("Obojętny do zasadowego"), ("neutral","alkaline") },
        { Normalize("Zasadowy"), ("alkaline","alkaline") },
        { Normalize("Obojętny do kwaśnego"), ("neutral","acidic") },
        { Normalize("Obojętny do kwaśnego"), ("neutral","acidic") },
        { Normalize("Obojętny do kwaśnego"), ("neutral","acidic") },
        { Normalize("Obojętny do kwaśnego"), ("neutral","acidic") },
    };

    public static readonly Dictionary<string, (string code, string namePl, string category)> SoilTraits = new(StringComparer.OrdinalIgnoreCase)
    {
        { Normalize("Uboga"), ("poor","Uboga","fertility") },
        { Normalize("Przeciętna"), ("average","Przeciętna","fertility") },
        { Normalize("Żyzna"), ("fertile","Żyzna","fertility") },

        { Normalize("Piaszczysta"), ("sandy","Piaszczysta","texture") },
        { Normalize("Gliniasta"), ("clay","Gliniasta","texture") },
        { Normalize("Piaszczysto-gliniasta"), ("sandy_loam","Piaszczysto-gliniasta","texture") },
        { Normalize("Przepuszczalna"), ("permeable","Przepuszczalna","texture") },

        { Normalize("Wapienna"), ("calcareous","Wapienna","chemistry") },

        { Normalize("Sucha"), ("dry","Sucha","drainage") },
        { Normalize("Umiarkowanie wilgotna"), ("moderately_moist","Umiarkowanie wilgotna","drainage") },
        { Normalize("Wilgotna"), ("moist","Wilgotna","drainage") },
        { Normalize("Mokra"), ("wet","Mokra","drainage") },
        { Normalize("Bagienna"), ("boggy","Bagienna","drainage") },

        { Normalize("Tolerancyjna"), ("tolerant","Tolerancyjna","tolerance") },
        { Normalize("Toleruje wilgoć"), ("tolerates_moisture","Toleruje wilgoć","tolerance") },
        { Normalize("Toleruje suszę"), ("drought_tolerant","Toleruje suszę","tolerance") },
    };

    public static readonly Dictionary<string, (string code, string namePl, string group)> Features = new(StringComparer.OrdinalIgnoreCase)
    {
        { Normalize("Pachnące"), ("fragrant","Pachnące","flower") },
        { Normalize("Miododajne"), ("nectar_rich","Miododajne","flower") },
        { Normalize("w gronach"), ("in_racemes","w gronach","flower") },
        { Normalize("Bardzo ozdobne"), ("showy","Bardzo ozdobne","flower") },
        { Normalize("Kotki"), ("catkins","Kotki","flower") },
        { Normalize("Pełne"), ("double","Pełne","flower") },

        { Normalize("biała kora"), ("white_bark","biała kora","bark") },
        { Normalize("gładka kora"), ("smooth_bark","gładka kora","bark") },

        { Normalize("Dłoniasto-klapowane"), ("palmately_lobed","Dłoniasto-klapowane","leaf") },
        { Normalize("Ząbkowane"), ("serrated","Ząbkowane","leaf") },
        { Normalize("Purpurowe"), ("purple_leaves","Purpurowe","leaf") },
        { Normalize("Złote liście"), ("golden_leaves","Złote liście","leaf") },
        { Normalize("płaczące pędy"), ("weeping_branches","płaczące pędy","leaf") },

        { Normalize("Szyszki"), ("cones","Szyszki","fruit") },
        { Normalize("Skrzydlaki"), ("samaras","Skrzydlaki","fruit") },
        { Normalize("Orzeszki"), ("nuts","Orzeszki","fruit") },
    };

    public static readonly Dictionary<string, string> ColorCanonical = new(StringComparer.OrdinalIgnoreCase)
    {
        { Normalize("Żółty"), "yellow" },
        { Normalize("Złoto-żółty"), "golden_yellow" },
        { Normalize("Złocistożółty"), "golden_yellow" },
        { Normalize("Miedzianobrązowy"), "copper_brown" },
        { Normalize("Miedziany"), "copper" },
        { Normalize("Brązowy"), "brown" },
        { Normalize("Żółtozielony"), "yellow_green" },
        { Normalize("Białe"), "white" },
        { Normalize("Różowe"), "pink" },
        { Normalize("Ciemnoróżowe"), "dark_pink" },
        { Normalize("Czerwone"), "red" },
        { Normalize("Pomarańczowo-czerwony"), "orange_red" },
        { Normalize("Czarne"), "black" },
        { Normalize("Zielono-żółte"), "yellow_green" },
        { Normalize("Pomarańczowoczerwony"), "orange_red" },
        { Normalize("Żółtawy"), "yellowish" },
    };

    public static string Normalize(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var normalized = input.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var chars = normalized.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray();
        return new string(chars);
    }
}

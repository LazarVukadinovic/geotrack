using System.Globalization;
using System.Text;

namespace Geotrack.Infrastructure.Services;

internal static class ModerationTextNormalizer
{
    internal static string NormalizeForModeration(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var builder = new StringBuilder(value.Length);
        var lower = value.ToLowerInvariant().Normalize(NormalizationForm.FormD);

        foreach (var ch in lower)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.NonSpacingMark)
                continue;

            AppendNormalized(builder, ch);
        }

        return builder.ToString().Normalize(NormalizationForm.FormC).Trim();
    }

    private static void AppendNormalized(StringBuilder builder, char ch)
    {
        switch (ch)
        {
            case '@':
                builder.Append('a');
                return;
            case '$':
                builder.Append('s');
                return;
            case '!':
            case '|':
                builder.Append('i');
                return;
            case '+':
                builder.Append('t');
                return;
            case '\u0111':
            case '\u0452':
                builder.Append("dj");
                return;
            case '\u0459':
                builder.Append("lj");
                return;
            case '\u045A':
                builder.Append("nj");
                return;
            case '\u045F':
                builder.Append("dz");
                return;
            case '\u0430':
                builder.Append('a');
                return;
            case '\u0431':
                builder.Append('b');
                return;
            case '\u0432':
                builder.Append('v');
                return;
            case '\u0433':
                builder.Append('g');
                return;
            case '\u0434':
                builder.Append('d');
                return;
            case '\u0435':
                builder.Append('e');
                return;
            case '\u0436':
            case '\u0437':
                builder.Append('z');
                return;
            case '\u0438':
                builder.Append('i');
                return;
            case '\u0458':
                builder.Append('j');
                return;
            case '\u043A':
                builder.Append('k');
                return;
            case '\u043B':
                builder.Append('l');
                return;
            case '\u043C':
                builder.Append('m');
                return;
            case '\u043D':
                builder.Append('n');
                return;
            case '\u043E':
                builder.Append('o');
                return;
            case '\u043F':
                builder.Append('p');
                return;
            case '\u0440':
                builder.Append('r');
                return;
            case '\u0441':
                builder.Append('s');
                return;
            case '\u0442':
                builder.Append('t');
                return;
            case '\u0443':
                builder.Append('u');
                return;
            case '\u0444':
                builder.Append('f');
                return;
            case '\u0445':
                builder.Append('h');
                return;
            case '\u0446':
            case '\u0447':
            case '\u045B':
                builder.Append('c');
                return;
            case '\u0448':
                builder.Append('s');
                return;
        }

        if (ch is >= 'a' and <= 'z' or >= '0' and <= '9')
        {
            builder.Append(MapLeet(ch));
            return;
        }

        builder.Append(' ');
    }

    private static char MapLeet(char ch) => ch switch
    {
        '0' => 'o',
        '1' => 'i',
        '3' => 'e',
        '4' => 'a',
        '5' => 's',
        '7' => 't',
        '8' => 'b',
        _ => ch
    };
}

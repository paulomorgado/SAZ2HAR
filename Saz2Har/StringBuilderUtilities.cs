using System.Text;
using System.Threading;

namespace PauloMorgado.Tools.SazToHar;

internal static class StringBuilderUtilities
{
    private static StringBuilder? StringBuilder;

    public static StringBuilder Rent()
    {
        return Interlocked.Exchange(ref StringBuilder, null) ?? new();
    }

    public static string ToStringAndReturn(this StringBuilder stringBuilder)
    {
        var text = stringBuilder.ToString();
        stringBuilder.Clear();
        StringBuilder = stringBuilder;
        return text;
    }
}
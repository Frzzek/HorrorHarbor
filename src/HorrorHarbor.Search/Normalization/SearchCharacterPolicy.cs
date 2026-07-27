using System.Globalization;
using System.Text;

namespace HorrorHarbor.Search.Normalization;

internal static class SearchCharacterPolicy
{
    public static bool IsJoinControl(Rune rune) => rune.Value is 0x200C or 0x200D;

    public static bool IsUnsafeFormatCharacter(Rune rune) =>
        Rune.GetUnicodeCategory(rune) == UnicodeCategory.Format && !IsJoinControl(rune);
}

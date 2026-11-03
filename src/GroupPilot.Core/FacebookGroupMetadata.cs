using System.Globalization;
using System.Text.RegularExpressions;

namespace GroupPilot.Core;

public static class FacebookGroupMetadata
{
    public static void Apply(SocialGroup group, string cardText)
    {
        group.FacebookCardText = cardText.Trim();
        var text = cardText.Replace('\u00a0', ' ').Replace('\u202f', ' ');
        var members = Regex.Match(text,
            @"(?<![\d.,])(?<count>\d+(?:[., ]\d+)*)(?:\s*(?<scale>[kKmM]|mil\.?|mii?))?\s+(?:de\s+)?(?:members?|membr(?:i|u))\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (members.Success)
        {
            var count = members.Groups["count"].Value.Replace(" ", "");
            var scale = members.Groups["scale"].Value.ToLowerInvariant();
            count = scale.Length == 0 ? count.Replace(".", "").Replace(",", "") : count.Replace(',', '.');
            var multiplier = scale is "m" or "mil" or "mil." ? 1_000_000 : scale.Length > 0 ? 1_000 : 1;
            if (decimal.TryParse(count, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
                && value <= long.MaxValue / multiplier)
                group.MemberCount = (long)decimal.Round(value * multiplier, 0);
        }

        group.FacebookVisibility = Regex.Match(text,
            @"(?:^|[\n\r·•])\s*(?:grup\s+)?(?<visibility>public|private|privat)(?:\s+(?:group|grup))?\s*(?=$|[\n\r·•])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Groups["visibility"].Value.ToLowerInvariant() switch
        {
            "public" => "Public",
            "private" or "privat" => "Private",
            _ => ""
        };
        group.FacebookActivity = Regex.Match(text,
            @"(?:^|[\n\r·•])\s*(?<activity>[^\n\r·•]*(?:posts? (?:a|per) |post[ăa]ri pe |posts? today|post[ăa]ri azi)[^\n\r·•]*)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Groups["activity"].Value.Trim();
        group.FacebookScannedAt = DateTimeOffset.UtcNow;
    }
}

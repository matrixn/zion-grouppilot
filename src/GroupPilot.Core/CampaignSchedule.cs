namespace GroupPilot.Core;

public static class CampaignSchedule
{
    public static string Label(CampaignRecurrence recurrence) => recurrence switch
    {
        CampaignRecurrence.Once => "Once",
        CampaignRecurrence.Daily => "Daily",
        CampaignRecurrence.EveryTwoDays => "Every 2 days",
        CampaignRecurrence.EveryThreeDays => "Every 3 days",
        CampaignRecurrence.Weekly => "Weekly",
        CampaignRecurrence.Monthly => "Monthly",
        CampaignRecurrence.TwiceMonthly => "Twice a month",
        CampaignRecurrence.ThreeTimesMonthly => "3 times a month",
        CampaignRecurrence.FourTimesMonthly => "4 times a month",
        _ => throw new ArgumentOutOfRangeException(nameof(recurrence))
    };

    public static DateTimeOffset? NextAfter(Campaign campaign, DateTimeOffset after)
    {
        if (campaign.Recurrence == CampaignRecurrence.Once)
            return null;
        var zone = TimeZoneInfo.FindSystemTimeZoneById(campaign.TimeZoneId);
        var anchor = TimeZoneInfo.ConvertTime((campaign.RecurrenceAnchorAt ?? campaign.ScheduledFor)
            ?? throw new ArgumentException("Recurring campaigns need a start date."), zone).DateTime;
        var localAfter = TimeZoneInfo.ConvertTime(after, zone).DateTime;
        var days = campaign.Recurrence switch
        {
            CampaignRecurrence.Daily => 1,
            CampaignRecurrence.EveryTwoDays => 2,
            CampaignRecurrence.EveryThreeDays => 3,
            CampaignRecurrence.Weekly => 7,
            _ => 0
        };
        if (days > 0)
        {
            var steps = Math.Max(0, (localAfter.Date - anchor.Date).Days / days);
            var candidate = anchor.AddDays(steps * days);
            while (Resolve(candidate, zone) <= after)
                candidate = candidate.AddDays(days);
            return Resolve(candidate, zone);
        }

        var repeats = campaign.Recurrence switch
        {
            CampaignRecurrence.Monthly => 1,
            CampaignRecurrence.TwiceMonthly => 2,
            CampaignRecurrence.ThreeTimesMonthly => 3,
            CampaignRecurrence.FourTimesMonthly => 4,
            _ => throw new ArgumentOutOfRangeException(nameof(campaign))
        };
        var month = new DateTime(localAfter.Year, localAfter.Month, 1);
        var anchorMonth = new DateTime(anchor.Year, anchor.Month, 1);
        if (month < anchorMonth)
            month = anchorMonth;
        while (true)
        {
            var monthDays = DateTime.DaysInMonth(month.Year, month.Month);
            for (var slot = 0; slot < repeats; slot++)
            {
                var day = repeats == 1 ? Math.Min(anchor.Day, monthDays) : 1 + slot * monthDays / repeats;
                var candidate = month.AddDays(day - 1).Add(anchor.TimeOfDay);
                var resolved = Resolve(candidate, zone);
                if (candidate >= anchor && resolved > after)
                    return resolved;
            }
            month = month.AddMonths(1);
        }
    }

    public static DateTimeOffset Resolve(DateTime local, TimeZoneInfo zone)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(local))
            local = local.AddMinutes(1);
        // The earlier occurrence is used when clocks move back.
        var offset = zone.IsAmbiguousTime(local) ? zone.GetAmbiguousTimeOffsets(local).Max() : zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset);
    }
}

using GroupPilot.Core;

namespace GroupPilot.Tests;

public sealed class CampaignScheduleTests
{
    [Theory]
    [InlineData(CampaignRecurrence.Daily, 1)]
    [InlineData(CampaignRecurrence.EveryTwoDays, 2)]
    [InlineData(CampaignRecurrence.EveryThreeDays, 3)]
    [InlineData(CampaignRecurrence.Weekly, 7)]
    public void DayIntervalsPreserveStartTime(CampaignRecurrence recurrence, int days)
    {
        var start = new DateTimeOffset(2026, 11, 4, 9, 30, 0, TimeSpan.Zero);
        var campaign = new Campaign { Recurrence = recurrence, ScheduledFor = start, TimeZoneId = TimeZoneInfo.Utc.Id };
        Assert.Equal(start.AddDays(days), CampaignSchedule.NextAfter(campaign, start));
        Assert.True(CampaignSchedule.NextAfter(campaign, start.AddDays(100)) > start.AddDays(100));
    }

    [Fact]
    public void MonthlyPreservesEndOfMonthAnchor()
    {
        var campaign = new Campaign
        {
            Recurrence = CampaignRecurrence.Monthly, TimeZoneId = TimeZoneInfo.Utc.Id,
            ScheduledFor = new DateTimeOffset(2027, 1, 31, 9, 0, 0, TimeSpan.Zero)
        };
        var february = new DateTimeOffset(2027, 2, 28, 9, 0, 0, TimeSpan.Zero);
        Assert.Equal(february, CampaignSchedule.NextAfter(campaign, campaign.ScheduledFor.Value));
        Assert.Equal(new DateTimeOffset(2027, 3, 31, 9, 0, 0, TimeSpan.Zero), CampaignSchedule.NextAfter(campaign, february));
    }

    [Theory]
    [InlineData(CampaignRecurrence.TwiceMonthly, 2)]
    [InlineData(CampaignRecurrence.ThreeTimesMonthly, 3)]
    [InlineData(CampaignRecurrence.FourTimesMonthly, 4)]
    public void CalendarMonthHasRequestedNumberOfSlots(CampaignRecurrence recurrence, int count)
    {
        var start = new DateTimeOffset(2027, 2, 1, 9, 0, 0, TimeSpan.Zero);
        var campaign = new Campaign { Recurrence = recurrence, ScheduledFor = start, TimeZoneId = TimeZoneInfo.Utc.Id };
        var dates = new HashSet<DateTimeOffset> { start };
        var current = start;
        for (var index = 0; index < count; index++)
        {
            current = CampaignSchedule.NextAfter(campaign, current)!.Value;
            if (current.Month == 2)
                Assert.True(dates.Add(current));
        }
        Assert.Equal(count, dates.Count);
        Assert.Equal(3, current.Month);
    }

    [Fact]
    public void DailyTimeFollowsDaylightSavingTransitions()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Bucharest");
        var start = CampaignSchedule.Resolve(new DateTime(2027, 3, 27, 9, 0, 0), zone);
        var campaign = new Campaign { Recurrence = CampaignRecurrence.Daily, ScheduledFor = start, TimeZoneId = zone.Id };
        var next = CampaignSchedule.NextAfter(campaign, start)!.Value;
        Assert.Equal(9, TimeZoneInfo.ConvertTime(next, zone).Hour);
        Assert.Equal(TimeSpan.FromHours(23), next - start);
    }
}

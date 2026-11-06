using ReciteHelper.Core.Scheduling;
using Xunit;

namespace ReciteHelper.Tests;

public class FsrsAlgorithmTests
{
    private static readonly SchedulerParameters P = SchedulerParameters.Default;
    private const double Epsilon = 1e-9;

    [Fact]
    public void Retrievability_AtElapsedEqualToStability_IsExactlyTargetRetention()
    {
        // Calibration property of the FSRS forgetting curve: R(S, S) = 0.9.
        foreach (var stability in new[] { 0.5, 1.0, 3.42, 30.0, 365.0 })
        {
            var recall = FsrsAlgorithm.Retrievability(stability, stability, P);
            Assert.Equal(0.9, recall, 9);
        }
    }

    [Fact]
    public void Retrievability_DecreasesMonotonicallyWithElapsedDays()
    {
        var previous = 1.0;
        for (var day = 1; day <= 120; day += 7)
        {
            var recall = FsrsAlgorithm.Retrievability(10.0, day, P);
            Assert.True(recall < previous, $"recall at day {day} should be lower than the previous sample");
            previous = recall;
        }
    }

    [Fact]
    public void IntervalForRetention_InvertsRetrievability()
    {
        // I(S) returns the day on which recall drops to the target retention.
        foreach (var stability in new[] { 0.5, 2.0, 17.5, 400.0 })
        {
            var interval = FsrsAlgorithm.IntervalForRetention(stability, 0.9, P);
            Assert.Equal(stability, interval, 6);
        }
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    [InlineData(-0.5)]
    public void IntervalForRetention_RejectsOutOfRangeRetention(double retention)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => FsrsAlgorithm.IntervalForRetention(10.0, retention, P));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public void InitialState_RejectsInvalidGrades(int grade)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FsrsAlgorithm.InitialState((ReviewGrade)grade, P));
    }

    [Fact]
    public void NextState_ClampsStabilityIntoAdmissibleRange_EvenForExtremeInputs()
    {
        var state = new MemoryState(2.0, 5.0);
        var after = FsrsAlgorithm.NextState(state, 36500.0 * 100, ReviewGrade.Good, P);

        Assert.InRange(after.Stability, FsrsAlgorithm.MinStability, FsrsAlgorithm.MaxStability);
        Assert.InRange(after.Difficulty, FsrsAlgorithm.MinDifficulty, FsrsAlgorithm.MaxDifficulty);
        Assert.True(double.IsFinite(after.Stability) && double.IsFinite(after.Difficulty));
    }

    [Fact]
    public void NextState_AfterLapse_DoesNotGrowStabilityBeyondPostLapseCeiling()
    {
        var state = new MemoryState(10.0, 5.0);
        var after = FsrsAlgorithm.NextState(state, 30.0, ReviewGrade.Again, P);

        Assert.True(after.Stability <= 10.0 + Epsilon, "a lapse must never increase stability");
    }

    [Fact]
    public void NextState_SameDayEasyReview_GrowsStability()
    {
        var state = new MemoryState(2.0, 5.0);
        var after = FsrsAlgorithm.NextState(state, 0.0, ReviewGrade.Easy, P);

        Assert.True(after.Stability > 2.0);
    }

    [Fact]
    public void Difficulty_StaysWithinBounds_UnderRepeatedSameGradeReviews()
    {
        var state = new MemoryState(2.0, 5.0);
        for (var i = 0; i < 200; i++)
            state = FsrsAlgorithm.NextState(state, 1.0, ReviewGrade.Again, P);

        Assert.InRange(state.Difficulty, FsrsAlgorithm.MinDifficulty, FsrsAlgorithm.MaxDifficulty);
    }
}

public class ReviewCalendarTests
{
    [Fact]
    public void ElapsedDays_SameMoment_IsZero()
    {
        var moment = new DateTime(2026, 11, 6, 21, 0, 0);
        Assert.Equal(0, ReviewCalendar.ElapsedDays(moment, moment));
    }

    [Fact]
    public void ElapsedDays_ClockAdjustmentBackwards_ClampsToZero()
    {
        var earlier = new DateTime(2026, 11, 5, 12, 0, 0);
        var later = new DateTime(2026, 11, 1, 12, 0, 0);
        Assert.Equal(0, ReviewCalendar.ElapsedDays(earlier, later));
    }

    [Fact]
    public void ElapsedDays_CrossingMidnightBeforeRollover_IsStillSameStudyDay()
    {
        // A session running from 23:00 to 02:00 counts as the same study day.
        var start = new DateTime(2026, 11, 5, 23, 0, 0);
        var finish = new DateTime(2026, 11, 6, 2, 0, 0);
        Assert.Equal(0, ReviewCalendar.ElapsedDays(start, finish));
    }

    [Fact]
    public void ElapsedDays_AfterRolloverHour_CountsAsNextStudyDay()
    {
        var start = new DateTime(2026, 11, 5, 23, 0, 0);
        var finish = new DateTime(2026, 11, 6, 5, 0, 0);
        Assert.Equal(1, ReviewCalendar.ElapsedDays(start, finish));
    }
}

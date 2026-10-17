using Temhalo.Core.Buffering;
using Temhalo.Core.Timing;
using Xunit;

namespace Temhalo.Presentation.Tests;

public sealed class DashboardTests
{
    [Fact]
    public void HistoryAvailabilityAndMissingTimePauseAreExplainedWithoutChangingMeasuredDelay()
    {
        var vm = new DashboardViewModel(_ => { });
        var delay = new FixedDelaySnapshot(30, 1048576, 1048576, 10485760, false, false, 10, 100, 90, 0, 0,
            false, false, false, 0, 25000, 25000, new DelayTransitionSnapshot(1, "IncreasingPause", 25000,
                true, 5000, 0, 0, 0, 0, false), HistoryAvailableMilliseconds: 25000);
        vm.Refresh(delay, true, false, true, true, true, "Relaying", 1, 1000000, 1, TimeSpan.FromSeconds(30));
        Assert.Equal("00:25", vm.CurrentText);
        Assert.Contains("00:25", vm.HistoryText);
        Assert.Contains("Historique insuffisant", vm.TransitionText);
        Assert.Contains("historique", vm.BufferHint);
    }
    [Fact]
    public void HotkeyAdjustmentsUseAppliedTargetAndClampWithoutDependingOnDraft()
    {
        var sent = new List<int>(); var vm = new DashboardViewModel(sent.Add);
        vm.RequestTarget(295); vm.NumberDraft = double.NaN;
        vm.AdjustTarget(10); Assert.Equal(300, vm.TargetSeconds);
        vm.RequestTarget(5); vm.NumberDraft = 42;
        vm.AdjustTarget(-10); Assert.Equal(0, vm.TargetSeconds); Assert.Equal(0, vm.NumberDraft);
        vm.CanEdit = false; vm.RequestTarget(60); Assert.Equal(0, vm.TargetSeconds);
        Assert.Equal(new[] { 295, 300, 5, 0 }, sent);
    }
    [Fact]
    public void DraftsDoNotSendAndInvalidNumbersCannotApply()
    {
        var sent = new List<int>(); var vm = new DashboardViewModel(sent.Add);
        vm.NumberDraft = 45;
        Assert.Empty(sent); Assert.Equal(45, vm.SliderDraft); Assert.Equal("00:00", vm.TargetText);
        foreach (double invalid in new[] { -1d, 301, 1.5, double.NaN, double.PositiveInfinity })
        { vm.NumberDraft = invalid; Assert.False(vm.ApplyCommand.CanExecute(null)); Assert.NotEmpty(vm.DraftError); }
        vm.SliderDraft = 46;
        vm.ApplyCommand.Execute(null);
        Assert.Equal(new[] { 46 }, sent); Assert.Equal("00:46", vm.TargetText);
    }

    [Fact]
    public void PresetsSendImmediatelyAndDrainDisablesEditing()
    {
        var sent = new List<int>(); var vm = new DashboardViewModel(sent.Add);
        vm.PresetCommand.Execute("30"); Assert.True(vm.Preset30); Assert.Equal(30, vm.NumberDraft);
        vm.Refresh(null, true, true, false, false, false, "Draining", 1, 0, 1, TimeSpan.Zero);
        vm.PresetCommand.Execute("0"); Assert.Equal(new[] { 30 }, sent); Assert.False(vm.CanEdit);
    }

    [Fact]
    public void TelemetryNeverOverwritesDraftAndRatesResetForNewAttempt()
    {
        var vm = new DashboardViewModel(_ => { }); vm.NumberDraft = 42;
        var delay = new FixedDelaySnapshot(15, 1048576, 1048576, 10485760, false, false, 10, 10, 10, 0, 0,
            false, false, false, 15000, 15000, 15000, new DelayTransitionSnapshot(1, "Stable", 15000, false, null, 0, 0, 0, 0, true), 1000000);
        vm.Refresh(delay, true, false, true, true, false, "Relaying", 1, 1000000, 1, TimeSpan.FromSeconds(20));
        Assert.Equal(42, vm.NumberDraft); Assert.Equal("00:15", vm.CurrentText); Assert.Equal("00:15", vm.TargetText);
        vm.Refresh(delay with { InputBytes = 2000000 }, true, false, true, true, false, "Relaying", 1, 2000000, 2, TimeSpan.FromSeconds(21));
        Assert.Equal("8,00 Mbit/s", vm.InputRate);
        vm.Refresh(delay, true, false, true, false, true, "Buffering", 2, 0, 3, TimeSpan.FromSeconds(22));
        Assert.Equal("0,00 Mbit/s", vm.InputRate); Assert.NotEqual("Émission confirmée", vm.OutputText);
    }

    [Theory]
    [InlineData(0, 2500)]
    [InlineData(15, 17500)]
    public void OutputBacklogKeepsActualMeasurementAndExplainsTheResidualDelay(int target, long age)
    {
        var vm = new DashboardViewModel(_ => { });
        var delay = new FixedDelaySnapshot(target, 1048576, 1048576, 10485760, false, false, 10, 100, 90, 0, 0,
            false, false, false, 0, age, age, new DelayTransitionSnapshot(1, "OutputBacklog", target * 1000L,
                false, null, 0, 0, 0, 0, false));
        vm.Refresh(delay, true, false, true, true, true, "Relaying", 1, 1000000, 1, TimeSpan.FromSeconds(20));
        Assert.Equal(target == 0 ? "00:02" : "00:17", vm.CurrentText);
        Assert.Contains("Sortie ralentie", vm.TransitionText);
        Assert.DoesNotContain("Délai atteint", vm.TransitionText);
        Assert.Equal(target == 0, vm.TransitionText.Contains("cliquez sur 0 s", StringComparison.Ordinal));
    }
}

using Pop.Core.Models;
using Pop.Core.Services;

namespace Pop.Tests;

public sealed class WindowEligibilityEvaluatorTests
{
    [Fact]
    public void Evaluate_RejectsCurrentProcessWindow()
    {
        var evaluator = new WindowEligibilityEvaluator();
        var traits = CreateTraits() with { IsCurrentProcessWindow = true };

        var result = evaluator.Evaluate(traits);

        Assert.False(result.IsSupported);
        Assert.Equal(WindowEligibilityReason.CurrentProcessWindow, result.Reason);
    }

    [Fact]
    public void Evaluate_RejectsMaximizedWindow()
    {
        var evaluator = new WindowEligibilityEvaluator();
        var traits = CreateTraits() with { IsMaximized = true };

        var result = evaluator.Evaluate(traits);

        Assert.False(result.IsSupported);
        Assert.Equal(WindowEligibilityReason.Maximized, result.Reason);
    }

    [Fact]
    public void Evaluate_AllowsSupportedWindow()
    {
        var evaluator = new WindowEligibilityEvaluator();
        var result = evaluator.Evaluate(CreateTraits());

        Assert.True(result.IsSupported);
        Assert.Equal(WindowEligibilityReason.Supported, result.Reason);
    }

    private static WindowTraits CreateTraits() => new(
        IsCaptionHit: true,
        IsVisible: true,
        IsResizable: true,
        IsMinimized: false,
        IsMaximized: false,
        IsStandardTopLevelWindow: true,
        IsFullscreen: false,
        IsElevated: false,
        IsCloaked: false,
        IsCurrentProcessWindow: false);
}

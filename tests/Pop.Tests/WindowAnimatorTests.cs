using System.Drawing;
using Pop.Core.Models;
using Pop.Core.Services;

namespace Pop.Tests;

public sealed class WindowAnimatorTests
{
    [Theory]
    [InlineData(900)]
    [InlineData(1800)]
    [InlineData(3600)]
    public void CreatePlan_GeneratesFrames_AndEndsOnExactTarget(double releaseVelocityX)
    {
        var animator = new WindowAnimator();
        var startBounds = new Rectangle(400, 160, 900, 620);
        var targetBounds = new Rectangle(0, 0, 960, 1040);

        var plan = animator.CreatePlan(startBounds, targetBounds, releaseVelocityX, 240);

        Assert.NotEmpty(plan.Frames);
        Assert.Equal(targetBounds, plan.FinalBounds);
        Assert.Equal(targetBounds, plan.Frames[^1].Bounds);
    }

    [Fact]
    public void CreatePlan_IncreasesOvershootCap_ForHigherVelocity()
    {
        var animator = new WindowAnimator();
        var startBounds = new Rectangle(400, 160, 900, 620);
        var targetBounds = new Rectangle(0, 0, 960, 1040);

        var slowPlan = animator.CreatePlan(startBounds, targetBounds, 900, 240);
        var fastPlan = animator.CreatePlan(startBounds, targetBounds, 3600, 240);

        Assert.True(fastPlan.MaxOvershootPx >= slowPlan.MaxOvershootPx);
    }
}

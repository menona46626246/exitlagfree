using System;
using GameRouteOptimizer.Core;
using Xunit;

namespace GameRouteOptimizer.Tests;

public class SmokeTests
{
    [Fact]
    public void ProductInfo_IsDefined()
    {
        Assert.Equal("GameRoute Optimizer", ProductInfo.Name);
    }

    [Fact]
    public void SmokeTest_Runs()
    {
        Assert.True(true);
    }
}

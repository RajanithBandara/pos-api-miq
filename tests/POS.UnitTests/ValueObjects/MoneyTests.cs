using System;
using FluentAssertions;
using POS.Domain.ValueObjects;
using Xunit;

namespace POS.UnitTests.ValueObjects;

public class MoneyTests
{
    [Fact]
    public void Addition_WithSameCurrency_ShouldSumAmounts()
    {
        var m1 = new Money(10.50m, "USD");
        var m2 = new Money(5.25m, "USD");

        var result = m1 + m2;

        result.Amount.Should().Be(15.75m);
        result.Currency.Should().Be("USD");
    }

    [Fact]
    public void Addition_WithDifferentCurrencies_ShouldThrowInvalidOperationException()
    {
        var m1 = new Money(10.50m, "USD");
        var m2 = new Money(5.25m, "EUR");

        var act = () => { var _ = m1 + m2; };

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*different currencies*");
    }
}

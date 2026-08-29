using System;
using FluentAssertions;
using POS.Domain.Entities;
using POS.Domain.Enums;
using POS.Domain.Events;
using POS.Domain.Exceptions;
using Xunit;

namespace POS.UnitTests.Domain;

public class SaleTests
{
    [Fact]
    public void AddItem_ShouldCalculateTotalsCorrectly()
    {
        // Arrange
        var sale = new Sale
        {
            StoreId = Guid.NewGuid(),
            PosTerminalId = Guid.NewGuid(),
            InvoiceNumber = "INV-001"
        };

        var item1 = new SaleItem
        {
            ProductId = Guid.NewGuid(),
            Sku = "SKU-1",
            ProductName = "Coffee",
            Quantity = 2,
            UnitCost = 1.0m,
            UnitPrice = 5.0m,
            DiscountAmount = 1.0m,
            TaxRate = 0.10m // 10%
        };
        item1.CalculateItemTotal();

        var item2 = new SaleItem
        {
            ProductId = Guid.NewGuid(),
            Sku = "SKU-2",
            ProductName = "Bagel",
            Quantity = 1,
            UnitCost = 0.5m,
            UnitPrice = 3.0m,
            DiscountAmount = 0.0m,
            TaxRate = 0.05m // 5%
        };
        item2.CalculateItemTotal();

        // Act
        sale.AddItem(item1);
        sale.AddItem(item2);

        // SubTotal = (2 * 5) + (1 * 3) = 13.00
        // DiscountTotal = 1.0 + 0.0 = 1.00
        // Tax for item1: (10 - 1) * 0.10 = 0.90
        // Tax for item2: 3 * 0.05 = 0.15
        // TaxTotal = 1.05
        // GrandTotal = 13 - 1 + 1.05 = 13.05

        // Assert
        sale.SubTotal.Should().Be(13.00m);
        sale.DiscountTotal.Should().Be(1.00m);
        sale.TaxTotal.Should().Be(1.05m);
        sale.GrandTotal.Should().Be(13.05m);
    }

    [Fact]
    public void MarkCompleted_ShouldEmitSaleCompletedDomainEvent()
    {
        // Arrange
        var sale = new Sale
        {
            StoreId = Guid.NewGuid(),
            PosTerminalId = Guid.NewGuid(),
            InvoiceNumber = "INV-002"
        };

        var item = new SaleItem
        {
            ProductId = Guid.NewGuid(),
            Quantity = 1,
            UnitPrice = 10m,
            TaxRate = 0
        };
        item.CalculateItemTotal();
        sale.AddItem(item);

        var payment = new Payment
        {
            Method = PaymentMethod.Cash,
            Amount = 10m,
            Status = PaymentStatus.Completed
        };
        sale.AddPayment(payment);

        // Act
        sale.MarkCompleted();

        // Assert
        sale.Status.Should().Be(SaleStatus.Completed);
        sale.DomainEvents.Should().ContainSingle(e => e is SaleCompletedDomainEvent);
        var domainEvent = System.Linq.Enumerable.First(sale.DomainEvents) as SaleCompletedDomainEvent;
        domainEvent.Should().NotBeNull();
        domainEvent!.GrandTotal.Should().Be(10m);
        domainEvent.InvoiceNumber.Should().Be("INV-002");
    }

    [Fact]
    public void VoidSale_ShouldUpdateStatusAndNotes()
    {
        // Arrange
        var sale = new Sale { Status = SaleStatus.Completed };

        // Act
        sale.VoidSale("Customer cancelled order");

        // Assert
        sale.Status.Should().Be(SaleStatus.Voided);
        sale.Notes.Should().Contain("Voided: Customer cancelled order");
    }
}

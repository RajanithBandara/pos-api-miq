using System;

namespace POS.Domain.ValueObjects;

public record BarcodeValue
{
    public string Code { get; init; }
    public string Format { get; init; }

    public BarcodeValue(string code, string format = "EAN13")
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Barcode value cannot be empty.", nameof(code));

        Code = code.Trim();
        Format = format.Trim().ToUpperInvariant();
    }

    public override string ToString() => Code;
}

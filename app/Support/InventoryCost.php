<?php

namespace App\Support;

use InvalidArgumentException;

final class InventoryCost
{
    public const UNIT_SCALE = 6;

    public static function unitCostFromValue(string|int $value, string|int $quantity): string
    {
        $quantityScaled = Quantity::toScaled($quantity);

        if ($quantityScaled === 0) {
            return '0.000000';
        }

        $valueMinor = Money::toMinor($value);
        $numerator = $valueMinor * 100000000;
        $negative = $numerator < 0 xor $quantityScaled < 0;
        $absoluteNumerator = abs($numerator);
        $absoluteQuantity = abs($quantityScaled);
        $scaled = intdiv($absoluteNumerator + intdiv($absoluteQuantity, 2), $absoluteQuantity);

        return Quantity::fromScaled($negative ? -$scaled : $scaled, self::UNIT_SCALE);
    }

    public static function valueForQuantity(string|int $unitCost, string|int $quantity): string
    {
        $unitScaled = Quantity::toScaled($unitCost, self::UNIT_SCALE);
        $quantityScaled = Quantity::toScaled($quantity);
        $product = $unitScaled * $quantityScaled;
        $divisor = 100000000;
        $negative = $product < 0;
        $minor = intdiv(abs($product) + intdiv($divisor, 2), $divisor);

        return Money::fromMinor($negative ? -$minor : $minor);
    }

    public static function validateNonNegativeValue(string|int $value): void
    {
        if (Money::toMinor($value) < 0) {
            throw new InvalidArgumentException('Inventory value cannot be negative for a receipt.');
        }
    }
}

<?php

namespace App\Support;

use InvalidArgumentException;

final class Quantity
{
    public const SCALE = 4;

    public const FACTOR_SCALE = 6;

    public static function normalize(string|int $value): string
    {
        return self::fromScaled(self::toScaled($value, self::SCALE), self::SCALE);
    }

    public static function factor(string|int $value): string
    {
        return self::fromScaled(self::toScaled($value, self::FACTOR_SCALE), self::FACTOR_SCALE);
    }

    public static function add(string|int $left, string|int $right): string
    {
        return self::fromScaled(
            self::toScaled($left, self::SCALE) + self::toScaled($right, self::SCALE),
            self::SCALE,
        );
    }

    public static function subtract(string|int $left, string|int $right): string
    {
        return self::fromScaled(
            self::toScaled($left, self::SCALE) - self::toScaled($right, self::SCALE),
            self::SCALE,
        );
    }

    public static function multiplyByFactor(string|int $quantity, string|int $factor): string
    {
        $quantityScaled = self::toScaled($quantity, self::SCALE);
        $factorScaled = self::toScaled($factor, self::FACTOR_SCALE);

        if ($quantityScaled < 0 || $factorScaled <= 0) {
            throw new InvalidArgumentException('Quantity must be non-negative and conversion factor must be positive.');
        }

        $product = $quantityScaled * $factorScaled;
        $divisor = 10 ** self::FACTOR_SCALE;
        $rounded = intdiv($product + intdiv($divisor, 2), $divisor);

        return self::fromScaled($rounded, self::SCALE);
    }

    public static function multiply(string|int $left, string|int $right): string
    {
        $leftScaled = self::toScaled($left, self::SCALE);
        $rightScaled = self::toScaled($right, self::SCALE);
        $product = $leftScaled * $rightScaled;
        $divisor = 10 ** self::SCALE;
        $rounded = $product >= 0
            ? intdiv($product + intdiv($divisor, 2), $divisor)
            : -intdiv(abs($product) + intdiv($divisor, 2), $divisor);

        return self::fromScaled($rounded, self::SCALE);
    }

    public static function multiplyMoney(string|int $unitCost, string|int $quantity): string
    {
        $minor = Money::toMinor($unitCost);
        $quantityScaled = self::toScaled($quantity, self::SCALE);
        $divisor = 10 ** self::SCALE;
        $product = $minor * $quantityScaled;
        $rounded = $product >= 0
            ? intdiv($product + intdiv($divisor, 2), $divisor)
            : -intdiv(abs($product) + intdiv($divisor, 2), $divisor);

        return Money::fromMinor($rounded);
    }

    public static function toScaled(string|int $value, int $scale = self::SCALE): int
    {
        $normalized = trim((string) $value);

        if (! preg_match('/^-?\d+(?:\.\d+)?$/', $normalized)) {
            throw new InvalidArgumentException('Invalid decimal quantity.');
        }

        $negative = str_starts_with($normalized, '-');
        $unsigned = ltrim($normalized, '-');
        [$whole, $fraction] = array_pad(explode('.', $unsigned, 2), 2, '');

        if (strlen($fraction) > $scale) {
            $discarded = substr($fraction, $scale);

            if (trim($discarded, '0') !== '') {
                throw new InvalidArgumentException("Quantity has more than {$scale} decimal places.");
            }

            $fraction = substr($fraction, 0, $scale);
        }

        $fraction = str_pad($fraction, $scale, '0');
        $factor = 10 ** $scale;
        $scaled = ((int) $whole * $factor) + (int) $fraction;

        return $negative ? -$scaled : $scaled;
    }

    public static function fromScaled(int $scaled, int $scale = self::SCALE): string
    {
        $sign = $scaled < 0 ? '-' : '';
        $absolute = abs($scaled);
        $factor = 10 ** $scale;

        return $sign.intdiv($absolute, $factor).'.'.str_pad(
            (string) ($absolute % $factor),
            $scale,
            '0',
            STR_PAD_LEFT,
        );
    }
}

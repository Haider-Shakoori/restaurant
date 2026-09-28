<?php

namespace App\Support;

use InvalidArgumentException;

final class Money
{
    public static function toMinor(string|int $amount): int
    {
        return self::toScaledInteger($amount, 2);
    }

    public static function fromMinor(int $minor): string
    {
        $sign = $minor < 0 ? '-' : '';
        $absolute = abs($minor);

        return $sign.intdiv($absolute, 100).'.'.str_pad((string) ($absolute % 100), 2, '0', STR_PAD_LEFT);
    }

    public static function add(string|int ...$amounts): string
    {
        $minor = 0;

        foreach ($amounts as $amount) {
            $minor += self::toMinor($amount);
        }

        return self::fromMinor($minor);
    }

    public static function subtract(string|int $left, string|int $right): string
    {
        return self::fromMinor(self::toMinor($left) - self::toMinor($right));
    }

    public static function percentage(string|int $amount, string|int $percent): string
    {
        $percentHundredths = self::toScaledInteger($percent, 2);

        if ($percentHundredths < 0 || $percentHundredths > 10000) {
            throw new InvalidArgumentException('Percentage must be between 0 and 100.');
        }

        $product = self::toMinor($amount) * $percentHundredths;
        $rounded = intdiv($product + 5000, 10000);

        return self::fromMinor($rounded);
    }

    private static function toScaledInteger(string|int $value, int $scale): int
    {
        $normalized = trim((string) $value);

        if (! preg_match('/^-?\d+(?:\.\d+)?$/', $normalized)) {
            throw new InvalidArgumentException('Invalid decimal amount.');
        }

        $negative = str_starts_with($normalized, '-');
        $unsigned = ltrim($normalized, '-');
        [$whole, $fraction] = array_pad(explode('.', $unsigned, 2), 2, '');

        if (strlen($fraction) > $scale) {
            $discarded = substr($fraction, $scale);

            if (trim($discarded, '0') !== '') {
                throw new InvalidArgumentException("Value has more than {$scale} decimal places.");
            }

            $fraction = substr($fraction, 0, $scale);
        }

        $fraction = str_pad($fraction, $scale, '0');
        $factor = 10 ** $scale;
        $scaled = ((int) $whole * $factor) + (int) $fraction;

        return $negative ? -$scaled : $scaled;
    }
}

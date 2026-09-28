<?php

namespace App\Support;

use InvalidArgumentException;

final class Money
{
    public static function toMinor(string|int|float $amount): int
    {
        $normalized = number_format((float) $amount, 2, '.', '');

        [$whole, $fraction] = explode('.', $normalized);

        return ((int) $whole * 100) + ((int) $fraction * ((int) $whole < 0 ? -1 : 1));
    }

    public static function fromMinor(int $minor): string
    {
        $sign = $minor < 0 ? '-' : '';
        $absolute = abs($minor);

        return $sign.intdiv($absolute, 100).'.'.str_pad((string) ($absolute % 100), 2, '0', STR_PAD_LEFT);
    }

    public static function add(string|int|float ...$amounts): string
    {
        return self::fromMinor(array_sum(array_map(self::toMinor(...), $amounts)));
    }

    public static function subtract(string|int|float $left, string|int|float $right): string
    {
        return self::fromMinor(self::toMinor($left) - self::toMinor($right));
    }

    public static function percentage(string|int|float $amount, string|int|float $percent): string
    {
        $basisPoints = (int) round((float) $percent * 100);

        if ($basisPoints < 0 || $basisPoints > 10000) {
            throw new InvalidArgumentException('Percentage must be between 0 and 100.');
        }

        return self::fromMinor((int) round(self::toMinor($amount) * $basisPoints / 10000));
    }
}

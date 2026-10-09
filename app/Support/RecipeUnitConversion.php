<?php

namespace App\Support;

use Illuminate\Validation\ValidationException;

final class RecipeUnitConversion
{
    /**
     * Recipe entry may use the common metric unit of the same physical
     * dimension. Stored consumption always uses the ingredient base unit.
     */
    public static function toBase(string $quantity, string $unit, string $baseUnit): string
    {
        $unit = strtolower(trim($unit));
        $baseUnit = strtolower(trim($baseUnit));

        $factors = [
            'kg' => 1000,
            'g' => 1,
            'l' => 1000,
            'ml' => 1,
            'pcs' => 1,
        ];
        $family = [
            'kg' => 'weight',
            'g' => 'weight',
            'l' => 'volume',
            'ml' => 'volume',
            'pcs' => 'count',
        ];
        if (! isset($family[$unit], $family[$baseUnit]) ||
            $family[$unit] !== $family[$baseUnit]) {
            throw ValidationException::withMessages([
                'items' => "Unit {$unit} is incompatible with ingredient stock unit {$baseUnit}.",
            ]);
        }

        try {
            $normalized = Quantity::normalize($quantity);
            if (Quantity::toScaled($normalized) <= 0) {
                throw new \InvalidArgumentException('Quantity must be positive.');
            }
            if ($unit === 'pcs' && Quantity::toScaled($normalized) % 10000 !== 0) {
                throw new \InvalidArgumentException('Pieces require a whole number.');
            }

            $conversionFactor = Quantity::factor((string) ($factors[$unit] / $factors[$baseUnit]));
            $base = Quantity::multiplyByFactor($normalized, $conversionFactor);
            if (Quantity::toScaled($base) <= 0) {
                throw new \InvalidArgumentException('Quantity rounds to zero at stock precision.');
            }

            return $base;
        } catch (\InvalidArgumentException $error) {
            throw ValidationException::withMessages([
                'items' => $error->getMessage(),
            ]);
        }
    }
}

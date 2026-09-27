<?php

namespace App\Enums;

enum BillingCycle: string
{
    case Monthly = 'monthly';
    case Quarterly = 'quarterly';
    case SemiAnnual = 'semiannual';
    case Annual = 'annual';
    case Custom = 'custom';

    public function label(): string
    {
        return match ($this) {
            self::Monthly => 'Monthly',
            self::Quarterly => 'Quarterly',
            self::SemiAnnual => 'Semiannual',
            self::Annual => 'Annual',
            self::Custom => 'Custom',
        };
    }

    public function intervalMonths(): ?int
    {
        return match ($this) {
            self::Monthly => 1,
            self::Quarterly => 3,
            self::SemiAnnual => 6,
            self::Annual => 12,
            self::Custom => null,
        };
    }
}

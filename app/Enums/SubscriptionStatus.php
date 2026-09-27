<?php

namespace App\Enums;

enum SubscriptionStatus: string
{
    case Scheduled = 'scheduled';
    case Trial = 'trial';
    case Active = 'active';
    case Due = 'due';
    case Expired = 'expired';
    case Cancelled = 'cancelled';

    public function label(): string
    {
        return ucfirst($this->value);
    }
}

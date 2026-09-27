<?php

namespace App\Enums;

enum BusinessStatus: string
{
    case Provisioning = 'provisioning';
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

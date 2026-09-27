<?php

namespace App\Enums;

enum ProvisioningState: string
{
    case Pending = 'pending';
    case Database = 'database';
    case Migrating = 'migrating';
    case Domain = 'domain';
    case Tls = 'tls';
    case Ready = 'ready';
    case Failed = 'failed';

    public function label(): string
    {
        return match ($this) {
            self::Tls => 'TLS',
            default => ucfirst($this->value),
        };
    }
}

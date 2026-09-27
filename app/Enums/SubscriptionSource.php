<?php

namespace App\Enums;

enum SubscriptionSource: string
{
    case Trial = 'trial';
    case ManualRenewal = 'manual_renewal';
}

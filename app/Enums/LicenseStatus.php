<?php

namespace App\Enums;

enum LicenseStatus: string
{
    case Active = 'active';
    case Revoked = 'revoked';
}

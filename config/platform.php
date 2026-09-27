<?php

return [
    'trial_days' => (int) env('PLATFORM_TRIAL_DAYS', 7),

    'provisioning' => [
        'stale_after_minutes' => (int) env('PLATFORM_PROVISIONING_STALE_MINUTES', 30),
    ],

    'reserved_subdomains' => [
        'admin',
        'api',
        'app',
        'assets',
        'billing',
        'businessos',
        'cpanel',
        'ftp',
        'mail',
        'platform',
        'smtp',
        'support',
        'webmail',
        'www',
    ],
];

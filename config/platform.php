<?php

return [
    'trial_days' => (int) env('PLATFORM_TRIAL_DAYS', 7),
    'renewal_contact' => env('PLATFORM_RENEWAL_CONTACT'),

    'subscription' => [
        'due_days' => (int) env('PLATFORM_SUBSCRIPTION_DUE_DAYS', 0),
    ],

    'provisioning' => [
        'stale_after_minutes' => (int) env('PLATFORM_PROVISIONING_STALE_MINUTES', 30),
        'tenant_domain_suffix' => env('PLATFORM_TENANT_DOMAIN_SUFFIX', 'restaurant.businessos.af'),
        'cpanel' => [
            'enabled' => (bool) env('TENANT_CPANEL_DATABASE_MANAGER', false),
            'database_user' => env('TENANT_CPANEL_DATABASE_USER', env('DB_USERNAME')),
            'uapi_binary' => env('TENANT_CPANEL_UAPI_BINARY', '/usr/bin/uapi'),
            'timeout_seconds' => (int) env('TENANT_CPANEL_UAPI_TIMEOUT_SECONDS', 60),
        ],
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

<?php

return [
    'key_prefix' => env('PLATFORM_LICENSE_PREFIX', 'RST'),
    'schema_version' => (int) env('PLATFORM_LICENSE_SCHEMA_VERSION', 1),
    'offline_grace_days' => (int) env('PLATFORM_LICENSE_OFFLINE_GRACE_DAYS', 7),
    'default_max_devices' => (int) env('PLATFORM_LICENSE_DEFAULT_MAX_DEVICES', 5),
    'default_max_mobile_devices' => (int) env('PLATFORM_LICENSE_DEFAULT_MAX_MOBILE_DEVICES', 3),

    'signing' => [
        'algorithm' => 'Ed25519',
        'key_id' => env('PLATFORM_LICENSE_SIGNING_KEY_ID', 'restaurant-v1'),
        'private_key' => env('PLATFORM_LICENSE_SIGNING_PRIVATE_KEY'),
        'public_key' => env('PLATFORM_LICENSE_SIGNING_PUBLIC_KEY'),
        'private_key_path' => env(
            'PLATFORM_LICENSE_SIGNING_PRIVATE_KEY_PATH',
            storage_path('app/private/keys/license-ed25519.secret')
        ),
        'public_key_path' => env(
            'PLATFORM_LICENSE_SIGNING_PUBLIC_KEY_PATH',
            storage_path('app/private/keys/license-ed25519.public')
        ),
    ],

    'hash_pepper' => env('PLATFORM_LICENSE_HASH_PEPPER'),
    'device_secret_pepper' => env('PLATFORM_DEVICE_SECRET_PEPPER'),
];

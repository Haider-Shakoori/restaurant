<?php

return [
    'currency' => env('RESTAURANT_CURRENCY', 'AFN'),
    'timezone' => env('RESTAURANT_TIMEZONE', 'Asia/Kabul'),
    'default_locale' => env('RESTAURANT_LOCALE', 'en'),

    'locales' => [
        'en' => ['name' => 'English', 'direction' => 'ltr'],
        'fa' => ['name' => 'Dari', 'direction' => 'rtl'],
        'ps' => ['name' => 'Pashto', 'direction' => 'rtl'],
    ],

    'api' => [
        'version' => env('RESTAURANT_API_VERSION', 'v1'),
    ],

    'performance' => [
        'low_bandwidth_mode' => (bool) env('RESTAURANT_LOW_BANDWIDTH_MODE', true),
        'max_page_size' => (int) env('RESTAURANT_MAX_PAGE_SIZE', 100),
        'sync_batch_size' => (int) env('RESTAURANT_SYNC_BATCH_SIZE', 100),
    ],

    'release' => env('APP_RELEASE', 'development'),
];

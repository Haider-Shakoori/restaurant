<?php

namespace App\Support;

/**
 * Tenant permission catalog and the default grants for system roles.
 *
 * Permission keys are the single source of truth for the tenant permission
 * matrix. Role grants are stored per tenant in the tenant `roles` table and
 * enforced server-side by the `tenant.permission` middleware.
 */
final class TenantPermissions
{
    /**
     * Permissions that only the owner (Super Admin) may grant or revoke.
     *
     * @var array<int, string>
     */
    public const PROTECTED = [
        'roles.manage',
        'permissions.manage',
        'settings.update',
    ];

    /**
     * System roles shipped with every restaurant tenant.
     *
     * `owner` is the protected Super Admin role: it always keeps every
     * permission and can never be edited or deleted.
     *
     * @return array<string, array{name: string, description: string}>
     */
    public static function systemRoles(): array
    {
        return [
            'owner' => [
                'name' => 'Super Admin',
                'description' => 'Restaurant owner with unrestricted access. Protected system role.',
            ],
            'admin' => [
                'name' => 'Administrator',
                'description' => 'Full restaurant administration including users, roles and settings.',
            ],
            'manager' => [
                'name' => 'Manager',
                'description' => 'Day-to-day operations, purchasing, expenses and reports.',
            ],
            'accountant' => [
                'name' => 'Accountant',
                'description' => 'Financial reports, expenses and accounting records.',
            ],
            'cashier' => [
                'name' => 'Cashier',
                'description' => 'POS billing, payments, discounts and cashier sessions.',
            ],
            'waiter' => [
                'name' => 'Waiter',
                'description' => 'Order taking, tables and service.',
            ],
            'kitchen' => [
                'name' => 'Kitchen Staff',
                'description' => 'Kitchen display, KOT preparation and readiness.',
            ],
            'expo' => [
                'name' => 'Expeditor',
                'description' => 'Kitchen expediting and pass coordination.',
            ],
            'runner' => [
                'name' => 'Runner',
                'description' => 'Runs ready dishes from the kitchen pass to tables.',
            ],
            'inventory' => [
                'name' => 'Inventory Manager',
                'description' => 'Stock, suppliers, purchases and recipes.',
            ],
            'auditor' => [
                'name' => 'Auditor',
                'description' => 'Read-only financial and operational review.',
            ],
        ];
    }

    /**
     * Permission groups rendered by the management UI.
     *
     * @return array<string, array{label: string, permissions: array<string, string>}>
     */
    public static function groups(): array
    {
        return [
            'dashboard' => [
                'label' => 'Dashboard',
                'permissions' => [
                    'dashboard.view' => 'View Dashboard',
                    'dashboard.revenue' => 'View Revenue',
                    'dashboard.profit-loss' => 'View Profit & Loss',
                ],
            ],
            'pos' => [
                'label' => 'POS & Orders',
                'permissions' => [
                    'pos.view' => 'View POS',
                    'orders.create' => 'Create Orders',
                    'orders.edit' => 'Edit Orders',
                    'orders.cancel' => 'Cancel Orders',
                    'orders.discount' => 'Apply Discounts',
                    'orders.payments' => 'Process Payments',
                    'orders.history' => 'View Order History',
                ],
            ],
            'tables' => [
                'label' => 'Tables',
                'permissions' => [
                    'tables.view' => 'View Tables',
                    'tables.assign' => 'Assign Tables',
                    'tables.status' => 'Change Table Status',
                    'tables.override' => 'Override Table Occupancy',
                ],
            ],
            'kitchen' => [
                'label' => 'Kitchen / KOT',
                'permissions' => [
                    'kitchen.view' => 'View KOT',
                    'kitchen.ready' => 'Mark KOT Ready',
                    'kitchen.history' => 'View Kitchen History',
                ],
            ],
            'inventory' => [
                'label' => 'Inventory',
                'permissions' => [
                    'inventory.view' => 'View Inventory',
                    'inventory.stock-add' => 'Add Stock',
                    'inventory.stock-edit' => 'Edit Stock',
                    'inventory.adjustments' => 'Record Stock Adjustments',
                    'inventory.settings' => 'Manage Inventory Settings',
                ],
            ],
            'purchases' => [
                'label' => 'Purchases',
                'permissions' => [
                    'purchases.view' => 'View Purchases',
                    'purchases.create' => 'Create Purchases',
                    'purchases.edit' => 'Edit Purchases',
                    'purchases.delete' => 'Delete Purchases',
                ],
            ],
            'expenses' => [
                'label' => 'Expenses',
                'permissions' => [
                    'expenses.view' => 'View Expenses',
                    'expenses.create' => 'Create Expenses',
                    'expenses.edit' => 'Edit Expenses',
                    'expenses.delete' => 'Delete Expenses',
                ],
            ],
            'reports' => [
                'label' => 'Reports',
                'permissions' => [
                    'reports.view' => 'View Reports',
                    'reports.export' => 'Export Reports',
                    'reports.financial' => 'View Financial Reports',
                ],
            ],
            'users' => [
                'label' => 'Users & Roles',
                'permissions' => [
                    'users.view' => 'View Users',
                    'users.create' => 'Create Users',
                    'users.edit' => 'Edit Users',
                    'users.disable' => 'Disable Users',
                    'roles.manage' => 'Manage Roles',
                    'permissions.manage' => 'Manage Permissions',
                ],
            ],
            'settings' => [
                'label' => 'Settings',
                'permissions' => [
                    'settings.view' => 'View Settings',
                    'settings.update' => 'Update Settings',
                ],
            ],
        ];
    }

    /**
     * @return array<int, string>
     */
    public static function allKeys(): array
    {
        $keys = [];

        foreach (self::groups() as $group) {
            foreach (array_keys($group['permissions']) as $key) {
                $keys[] = $key;
            }
        }

        return $keys;
    }

    public static function isProtected(string $permission): bool
    {
        return in_array($permission, self::PROTECTED, true);
    }

    /**
     * Default permission grants for a system role. Unknown roles receive no
     * permissions (deny by default).
     *
     * @return array<int, string>
     */
    public static function defaultPermissionsFor(string $roleKey): array
    {
        $all = self::allKeys();

        return match ($roleKey) {
            'owner', 'admin' => $all,
            'manager' => self::keysExcept([], $all),
            'accountant' => [
                'dashboard.view', 'dashboard.revenue', 'dashboard.profit-loss',
                'orders.history', 'purchases.view',
                'expenses.view', 'expenses.create', 'expenses.edit',
                'reports.view', 'reports.export', 'reports.financial',
            ],
            'cashier' => [
                'dashboard.view', 'dashboard.revenue',
                'pos.view', 'orders.create', 'orders.edit', 'orders.cancel',
                'orders.discount', 'orders.payments', 'orders.history',
                'tables.view', 'tables.status',
            ],
            'waiter' => [
                'dashboard.view', 'pos.view', 'orders.create', 'orders.edit', 'orders.history',
                'tables.view', 'tables.assign', 'tables.status', 'kitchen.view',
            ],
            'kitchen' => ['kitchen.view', 'kitchen.ready', 'kitchen.history'],
            'expo' => ['kitchen.view', 'kitchen.history', 'tables.view'],
            'runner' => ['kitchen.view', 'tables.view'],
            'inventory' => [
                'dashboard.view', 'inventory.view', 'inventory.stock-add', 'inventory.stock-edit',
                'inventory.adjustments', 'inventory.settings',
                'purchases.view', 'purchases.create', 'purchases.edit', 'purchases.delete',
                'reports.view',
            ],
            'auditor' => [
                'dashboard.view', 'dashboard.revenue', 'dashboard.profit-loss', 'orders.history',
                'inventory.view', 'purchases.view', 'expenses.view',
                'reports.view', 'reports.export', 'reports.financial',
            ],
            default => [],
        };
    }

    /**
     * @param  array<int, string>  $except
     * @param  array<int, string>  $from
     * @return array<int, string>
     */
    private static function keysExcept(array $except, array $from): array
    {
        return array_values(array_diff($from, $except));
    }
}

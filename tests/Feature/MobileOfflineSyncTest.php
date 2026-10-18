<?php

namespace Tests\Feature;

use App\Enums\BusinessStatus;
use App\Enums\PlatformRole;
use App\Enums\ProvisioningState;
use App\Models\AdminUser;
use App\Models\Business;
use App\Models\DesktopEntityLink;
use App\Models\DesktopOperationalRecord;
use App\Models\DiningArea;
use App\Models\DiningTable;
use App\Models\KitchenStation;
use App\Models\MenuCategory;
use App\Models\MenuItem;
use App\Models\MenuItemKitchenRoute;
use App\Models\MenuModifierGroup;
use App\Models\Order;
use App\Models\Plan;
use App\Models\RestaurantBranch;
use App\Models\SyncMutation;
use App\Models\Tenant;
use App\Models\TenantUser;
use App\Models\WaiterPushDevice;
use App\Services\Platform\LicenseService;
use App\Services\Platform\SubscriptionService;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Illuminate\Support\Carbon;
use Illuminate\Support\Facades\File;
use Illuminate\Support\Str;
use Tests\TestCase;

class MobileOfflineSyncTest extends TestCase
{
    use RefreshDatabase;

    /** @var array<int, string> */
    private array $tenantDatabases = [];

    /** @var array<int, string> */
    private array $tenantStoragePaths = [];

    protected function setUp(): void
    {
        parent::setUp();

        $keypair = sodium_crypto_sign_keypair();

        config([
            'license.signing.private_key' => base64_encode(sodium_crypto_sign_secretkey($keypair)),
            'license.signing.public_key' => base64_encode(sodium_crypto_sign_publickey($keypair)),
            'license.signing.key_id' => 'sync-test-ed25519-v1',
            'license.offline_grace_days' => 7,
            'license.default_max_devices' => 5,
        ]);
    }

    protected function tearDown(): void
    {
        Carbon::setTestNow();

        if (tenancy()->initialized) {
            tenancy()->end();
        }

        foreach ($this->tenantDatabases as $database) {
            @unlink(database_path($database));
        }

        foreach ($this->tenantStoragePaths as $path) {
            File::deleteDirectory($path);
        }

        parent::tearDown();
    }

    public function test_waiter_push_registration_is_device_authenticated_and_can_be_revoked(): void
    {
        [$business, $domain, $tenant] = $this->createActiveBusiness();
        $device = $this->activateDevice($business, $domain, 'push-registration-device');
        tenancy()->initialize($tenant);
        [$waiter] = $this->seedRestaurant('pushwaiter@restaurant.test');
        tenancy()->end();

        $token = $this->login($domain, 'pushwaiter@restaurant.test');
        $headers = $this->syncHeaders($token, $device);
        $pushToken = str_repeat('z', 75);

        $this->withHeaders($headers)->postJson("http://{$domain}/api/v1/push/devices", [
            'token' => $pushToken,
            'platform' => 'android',
        ])->assertOk()->assertJsonPath('status', 'registered');

        tenancy()->initialize($tenant);
        $registered = WaiterPushDevice::query()->firstOrFail();
        $this->assertSame((string) $waiter->id, (string) $registered->tenant_user_id);
        $this->assertSame($pushToken, $registered->fcm_token);
        $this->assertTrue($registered->enabled);
        tenancy()->end();

        $invalid = [...$headers, 'X-Device-Secret' => 'wrong-credential'];
        $this->withHeaders($invalid)->postJson("http://{$domain}/api/v1/push/devices", [
            'token' => str_repeat('b', 75),
            'platform' => 'ios',
        ])->assertUnprocessable();

        $this->withHeaders($headers)->deleteJson("http://{$domain}/api/v1/push/devices")
            ->assertOk()->assertJsonPath('status', 'unregistered');

        tenancy()->initialize($tenant);
        $this->assertFalse(WaiterPushDevice::query()->firstOrFail()->enabled);
    }

    public function test_device_bound_bootstrap_push_retry_and_incremental_pull_are_resumable(): void
    {
        [$business, $domain, $tenant] = $this->createActiveBusiness();
        $credentials = $this->activateDevice($business, $domain, 'sync-device-001');

        tenancy()->initialize($tenant);
        [$waiter, $table, $menuItem] = $this->seedRestaurant('waiter1@restaurant.test');
        $menuItem->update(['image_path' => 'menu-items/kabuli.webp']);
        tenancy()->end();

        $token = $this->login($domain, 'waiter1@restaurant.test');
        $headers = $this->syncHeaders($token, $credentials);

        $bootstrap = $this->withHeaders($headers)
            ->getJson("http://{$domain}/api/v1/sync/bootstrap")
            ->assertOk()
            ->assertJsonPath('data.tenant_id', $tenant->id)
            ->assertJsonCount(1, 'data.tables')
            ->assertJsonCount(1, 'data.menu')
            ->json('data');

        $this->assertStringStartsWith(
            "http://{$domain}/media/menu-items/{$menuItem->id}?v=",
            $bootstrap['menu'][0]['items'][0]['image_url'],
        );

        $cursor = $bootstrap['cursor'];

        $mutations = [
            [
                'mutation_id' => 'M-OPEN-001',
                'operation' => 'order.open',
                'occurred_at' => now()->utc()->toIso8601String(),
                'payload' => [
                    'client_order_id' => 'CLIENT-ORDER-001',
                    'dining_table_id' => $table->id,
                    'guest_count' => 2,
                ],
            ],
            [
                'mutation_id' => 'M-LINE-001',
                'operation' => 'order.item.add',
                'occurred_at' => now()->utc()->toIso8601String(),
                'payload' => [
                    'client_order_id' => 'CLIENT-ORDER-001',
                    'client_line_id' => 'CLIENT-LINE-001',
                    'menu_item_id' => $menuItem->id,
                    'quantity' => 2,
                ],
            ],
            [
                'mutation_id' => 'M-SUBMIT-001',
                'operation' => 'order.submit',
                'occurred_at' => now()->utc()->toIso8601String(),
                'payload' => [
                    'client_order_id' => 'CLIENT-ORDER-001',
                ],
            ],
        ];

        $firstPush = $this->withHeaders($headers)
            ->postJson("http://{$domain}/api/v1/sync/push", [
                'batch_id' => 'BATCH-001',
                'mutations' => $mutations,
            ])
            ->assertOk()
            ->json('data');

        $this->assertSame(
            ['accepted', 'accepted', 'accepted'],
            collect($firstPush['results'])->pluck('status')->all(),
        );

        $retry = $this->withHeaders($headers)
            ->postJson("http://{$domain}/api/v1/sync/push", [
                'batch_id' => 'BATCH-001-RETRY',
                'mutations' => $mutations,
            ])
            ->assertOk()
            ->json('data');

        $this->assertSame(
            ['accepted', 'accepted', 'accepted'],
            collect($retry['results'])->pluck('status')->all(),
        );

        $pull = $this->withHeaders($headers)
            ->getJson("http://{$domain}/api/v1/sync/pull?cursor={$cursor}&limit=100")
            ->assertOk()
            ->json('data');

        $this->assertGreaterThan($cursor, $pull['cursor']);
        $this->assertContains('order', collect($pull['changes'])->pluck('entity_type')->all());

        tenancy()->initialize($tenant);

        $order = Order::query()
            ->where('client_order_id', 'CLIENT-ORDER-001')
            ->firstOrFail();

        $this->assertSame(Order::STATUS_SUBMITTED, $order->status);
        $this->assertSame(1, Order::query()->count());
        $this->assertSame(1, $order->items()->count());
        $this->assertSame(3, SyncMutation::query()->count());
        $this->assertSame($waiter->id, $order->waiter_id);

        tenancy()->end();
    }

    public function test_manager_desktop_qr_pairing_token_is_one_time_and_activates_waiter_without_raw_license(): void
    {
        [$business, $domain, $tenant] = $this->createActiveBusiness();
        $desktop = $this->activateDevice(
            $business,
            $domain,
            'restaurant-desktop-pairing-001',
            'windows',
        );

        tenancy()->initialize($tenant);
        TenantUser::query()->create([
            'public_id' => (string) Str::ulid(),
            'name' => 'Restaurant Manager',
            'email' => 'manager@restaurant.test',
            'password' => 'password123',
            'is_active' => true,
            'role' => 'manager',
        ]);
        tenancy()->end();

        $managerToken = $this->login($domain, 'manager@restaurant.test');

        $pairing = $this->withHeaders($this->syncHeaders($managerToken, $desktop))
            ->postJson("http://{$domain}/api/v1/pairing-tokens")
            ->assertCreated()
            ->assertJsonPath('data.tenant_id', $tenant->id)
            ->json('data');

        $this->assertNotEmpty($pairing['pairing_token']);
        $this->assertNotEmpty($pairing['expires_at']);

        $activation = $this->postJson("http://{$domain}/api/v1/pairing-tokens/redeem", [
            'pairing_token' => $pairing['pairing_token'],
            'device_uid' => 'waiter-paired-mobile-001',
            'device_name' => 'Dining Room Tablet',
            'platform' => 'android',
            'app_version' => '1.0.0',
        ])->assertCreated();

        $this->assertSame('android', $activation->json('device.platform'));
        $this->assertNotEmpty($activation->json('device_secret'));
        $this->assertSame(
            $business->tenant_id,
            $activation->json('lease.payload.tenant_id'),
        );

        $this->postJson("http://{$domain}/api/v1/pairing-tokens/redeem", [
            'pairing_token' => $pairing['pairing_token'],
            'device_uid' => 'waiter-paired-mobile-002',
            'device_name' => 'Second Tablet',
            'platform' => 'ios',
        ])->assertUnprocessable()
            ->assertJsonValidationErrors('pairing_token');
    }

    public function test_refreshing_desktop_qr_invalidates_the_previous_pairing_token(): void
    {
        [$business, $domain, $tenant] = $this->createActiveBusiness();
        $desktop = $this->activateDevice(
            $business,
            $domain,
            'restaurant-desktop-pairing-refresh',
            'windows',
        );

        tenancy()->initialize($tenant);
        TenantUser::query()->create([
            'public_id' => (string) Str::ulid(),
            'name' => 'Restaurant Manager',
            'email' => 'manager-refresh@restaurant.test',
            'password' => 'password123',
            'is_active' => true,
            'role' => 'manager',
        ]);
        tenancy()->end();

        $managerToken = $this->login($domain, 'manager-refresh@restaurant.test');
        $headers = $this->syncHeaders($managerToken, $desktop);

        $first = $this->withHeaders($headers)
            ->postJson("http://{$domain}/api/v1/pairing-tokens")
            ->assertCreated()
            ->json('data.pairing_token');

        $second = $this->withHeaders($headers)
            ->postJson("http://{$domain}/api/v1/pairing-tokens")
            ->assertCreated()
            ->json('data.pairing_token');

        $this->assertNotSame($first, $second);

        $this->postJson("http://{$domain}/api/v1/pairing-tokens/redeem", [
            'pairing_token' => $first,
            'device_uid' => 'stale-qr-mobile',
            'platform' => 'android',
        ])->assertUnprocessable()
            ->assertJsonValidationErrors('pairing_token');

        $this->postJson("http://{$domain}/api/v1/pairing-tokens/redeem", [
            'pairing_token' => $second,
            'device_uid' => 'fresh-qr-mobile',
            'platform' => 'android',
        ])->assertCreated();
    }

    public function test_second_waiter_receives_deterministic_table_busy_conflict(): void
    {
        [$business, $domain, $tenant] = $this->createActiveBusiness();
        $credentials = $this->activateDevice($business, $domain, 'sync-device-002');

        tenancy()->initialize($tenant);
        [, $table] = $this->seedRestaurant('waiter1@restaurant.test');
        TenantUser::query()->create([
            'public_id' => (string) Str::ulid(),
            'name' => 'Waiter Two',
            'email' => 'waiter2@restaurant.test',
            'password' => 'password123',
            'is_active' => true,
            'role' => 'waiter',
        ]);
        tenancy()->end();

        $tokenOne = $this->login($domain, 'waiter1@restaurant.test');
        $tokenTwo = $this->login($domain, 'waiter2@restaurant.test');

        $this->withHeaders($this->syncHeaders($tokenOne, $credentials))
            ->postJson("http://{$domain}/api/v1/sync/push", [
                'batch_id' => 'BATCH-W1',
                'mutations' => [[
                    'mutation_id' => 'W1-OPEN',
                    'operation' => 'order.open',
                    'payload' => [
                        'client_order_id' => 'ORDER-W1',
                        'dining_table_id' => $table->id,
                    ],
                ]],
            ])
            ->assertOk()
            ->assertJsonPath('data.results.0.status', 'accepted');

        $this->withHeaders($this->syncHeaders($tokenTwo, $credentials))
            ->postJson("http://{$domain}/api/v1/sync/push", [
                'batch_id' => 'BATCH-W2',
                'mutations' => [[
                    'mutation_id' => 'W2-OPEN',
                    'operation' => 'order.open',
                    'payload' => [
                        'client_order_id' => 'ORDER-W2',
                        'dining_table_id' => $table->id,
                    ],
                ]],
            ])
            ->assertOk()
            ->assertJsonPath('data.results.0.status', 'conflict')
            ->assertJsonPath('data.results.0.code', 'table_busy');
    }

    public function test_reusing_mutation_id_with_different_payload_is_rejected(): void
    {
        [$business, $domain, $tenant] = $this->createActiveBusiness();
        $credentials = $this->activateDevice($business, $domain, 'sync-device-003');

        tenancy()->initialize($tenant);
        [, $table] = $this->seedRestaurant('waiter1@restaurant.test');
        tenancy()->end();

        $token = $this->login($domain, 'waiter1@restaurant.test');
        $headers = $this->syncHeaders($token, $credentials);

        $this->withHeaders($headers)
            ->postJson("http://{$domain}/api/v1/sync/push", [
                'batch_id' => 'BATCH-A',
                'mutations' => [[
                    'mutation_id' => 'MUTATION-SAME',
                    'operation' => 'order.open',
                    'payload' => [
                        'client_order_id' => 'ORDER-A',
                        'dining_table_id' => $table->id,
                    ],
                ]],
            ])
            ->assertOk()
            ->assertJsonPath('data.results.0.status', 'accepted');

        $this->withHeaders($headers)
            ->postJson("http://{$domain}/api/v1/sync/push", [
                'batch_id' => 'BATCH-B',
                'mutations' => [[
                    'mutation_id' => 'MUTATION-SAME',
                    'operation' => 'order.open',
                    'payload' => [
                        'client_order_id' => 'ORDER-B',
                        'dining_table_id' => $table->id,
                    ],
                ]],
            ])
            ->assertOk()
            ->assertJsonPath('data.results.0.status', 'rejected')
            ->assertJsonPath('data.results.0.code', 'mutation_id_reused');
    }

    public function test_bootstrap_includes_branches_staff_and_menu_modifiers(): void
    {
        [$business, $domain, $tenant] = $this->createActiveBusiness();
        $credentials = $this->activateDevice($business, $domain, 'sync-device-modifiers');

        tenancy()->initialize($tenant);
        [, , $menuItem] = $this->seedRestaurant('waiter1@restaurant.test');

        TenantUser::query()->create([
            'public_id' => (string) Str::ulid(),
            'name' => 'Restaurant Manager',
            'email' => 'manager@restaurant.test',
            'password' => 'password123',
            'is_active' => true,
            'role' => 'manager',
        ]);

        $group = MenuModifierGroup::query()->create([
            'name' => 'Size',
            'min_selections' => 1,
            'max_selections' => 1,
            'sort_order' => 1,
            'is_active' => true,
        ]);

        $group->options()->create([
            'name' => 'Large',
            'price_delta' => '50.00',
            'sort_order' => 1,
            'is_active' => true,
        ]);

        $menuItem->modifierGroups()->attach($group->id, ['sort_order' => 1]);

        $branch = RestaurantBranch::query()->firstOrFail();
        $station = KitchenStation::query()->create([
            'branch_id' => $branch->id,
            'code' => 'GRILL',
            'name' => 'Grill Station',
            'sort_order' => 1,
            'is_active' => true,
        ]);

        MenuItemKitchenRoute::query()->create([
            'menu_item_id' => $menuItem->id,
            'branch_id' => $branch->id,
            'kitchen_station_id' => $station->id,
        ]);

        tenancy()->end();

        $token = $this->login($domain, 'waiter1@restaurant.test');

        $this->withHeaders($this->syncHeaders($token, $credentials))
            ->getJson("http://{$domain}/api/v1/sync/bootstrap")
            ->assertOk()
            ->assertJsonCount(1, 'data.branches')
            ->assertJsonCount(2, 'data.staff')
            ->assertJsonPath('data.menu.0.items.0.modifier_groups.0.name', 'Size')
            ->assertJsonPath('data.menu.0.items.0.modifier_groups.0.min_selections', 1)
            ->assertJsonPath('data.menu.0.items.0.modifier_groups.0.options.0.name', 'Large')
            ->assertJsonPath('data.menu.0.items.0.modifier_groups.0.options.0.price_delta', '50.00')
            ->assertJsonCount(1, 'data.kitchen.stations')
            ->assertJsonCount(1, 'data.kitchen.routes')
            ->assertJsonPath('data.kitchen.stations.0.code', 'GRILL')
            ->assertJsonPath('data.kitchen.routes.0.menu_item_id', $menuItem->id)
            ->assertJsonPath('data.kitchen.routes.0.kitchen_station_id', $station->id);
    }

    public function test_bootstrap_only_exposes_active_tables_in_active_floors_and_includes_empty_floors(): void
    {
        [$business, $domain, $tenant] = $this->createActiveBusiness();
        $credentials = $this->activateDevice($business, $domain, 'sync-device-table-parity');

        tenancy()->initialize($tenant);
        [, $activeTable] = $this->seedRestaurant('parity-waiter@restaurant.test');
        $branch = $activeTable->diningArea->branch;

        DiningArea::query()->create([
            'branch_id' => $branch->id,
            'name' => 'Empty Upstairs',
            'sort_order' => 2,
            'is_active' => true,
        ]);
        $closedArea = DiningArea::query()->create([
            'branch_id' => $branch->id,
            'name' => 'Closed Terrace',
            'sort_order' => 3,
            'is_active' => false,
        ]);
        DiningTable::query()->create([
            'dining_area_id' => $closedArea->id,
            'code' => 'HIDDEN01',
            'name' => 'Hidden table',
            'capacity' => 2,
            'status' => DiningTable::STATUS_AVAILABLE,
            'is_active' => true,
        ]);
        DiningTable::query()->create([
            'dining_area_id' => $activeTable->dining_area_id,
            'code' => 'ARCHIVED01',
            'name' => 'Archived table',
            'capacity' => 2,
            'status' => DiningTable::STATUS_AVAILABLE,
            'is_active' => false,
        ]);
        tenancy()->end();

        $token = $this->login($domain, 'parity-waiter@restaurant.test');
        $this->withHeaders($this->syncHeaders($token, $credentials))
            ->getJson("http://{$domain}/api/v1/sync/bootstrap")
            ->assertOk()
            ->assertJsonCount(1, 'data.tables')
            ->assertJsonPath('data.tables.0.id', $activeTable->id)
            ->assertJsonCount(2, 'data.areas');
    }

    public function test_mobile_sync_supports_table_transfer_unsent_split_and_merge(): void
    {
        [$business, $domain, $tenant] = $this->createActiveBusiness();
        $credentials = $this->activateDevice($business, $domain, 'sync-device-ops');

        tenancy()->initialize($tenant);
        [, $table, $menuItem] = $this->seedRestaurant('waiter1@restaurant.test');
        $area = DiningArea::query()->firstOrFail();

        $tableTwo = DiningTable::query()->create([
            'dining_area_id' => $area->id,
            'code' => 'T-02',
            'name' => 'Table 2',
            'capacity' => 4,
            'status' => DiningTable::STATUS_AVAILABLE,
            'is_active' => true,
        ]);
        $tableThree = DiningTable::query()->create([
            'dining_area_id' => $area->id,
            'code' => 'T-03',
            'name' => 'Table 3',
            'capacity' => 4,
            'status' => DiningTable::STATUS_AVAILABLE,
            'is_active' => true,
        ]);

        tenancy()->end();

        $token = $this->login($domain, 'waiter1@restaurant.test');
        $headers = $this->syncHeaders($token, $credentials);

        $mutations = [
            [
                'mutation_id' => 'OPS-OPEN-SOURCE',
                'operation' => 'order.open',
                'payload' => [
                    'client_order_id' => 'OPS-SOURCE',
                    'dining_table_id' => $table->id,
                    'guest_count' => 2,
                ],
            ],
            [
                'mutation_id' => 'OPS-LINE-SOURCE',
                'operation' => 'order.item.add',
                'payload' => [
                    'client_order_id' => 'OPS-SOURCE',
                    'client_line_id' => 'OPS-SOURCE-LINE',
                    'menu_item_id' => $menuItem->id,
                    'quantity' => 2,
                ],
            ],
            [
                'mutation_id' => 'OPS-OPEN-TARGET',
                'operation' => 'order.open',
                'payload' => [
                    'client_order_id' => 'OPS-TARGET',
                    'dining_table_id' => $tableTwo->id,
                    'guest_count' => 1,
                ],
            ],
            [
                'mutation_id' => 'OPS-TRANSFER',
                'operation' => 'order.table.transfer',
                'payload' => [
                    'client_order_id' => 'OPS-SOURCE',
                    'target_table_id' => $tableThree->id,
                ],
            ],
            [
                'mutation_id' => 'OPS-MOVE',
                'operation' => 'order.item.move',
                'payload' => [
                    'source_client_order_id' => 'OPS-SOURCE',
                    'target_client_order_id' => 'OPS-TARGET',
                    'client_line_id' => 'OPS-SOURCE-LINE',
                    'target_client_line_id' => 'OPS-MOVED-LINE',
                    'quantity' => 1,
                ],
            ],
            [
                'mutation_id' => 'OPS-MERGE',
                'operation' => 'order.merge',
                'payload' => [
                    'source_client_order_id' => 'OPS-SOURCE',
                    'target_client_order_id' => 'OPS-TARGET',
                ],
            ],
        ];

        $result = $this->withHeaders($headers)
            ->postJson("http://{$domain}/api/v1/sync/push", [
                'batch_id' => 'OPS-BATCH',
                'mutations' => $mutations,
            ])
            ->assertOk()
            ->json('data.results');

        $this->assertSame(
            array_fill(0, 6, 'accepted'),
            collect($result)->pluck('status')->all(),
        );

        tenancy()->initialize($tenant);

        $source = Order::query()->where('client_order_id', 'OPS-SOURCE')->firstOrFail();
        $target = Order::query()->where('client_order_id', 'OPS-TARGET')->firstOrFail();

        $this->assertSame(Order::STATUS_CANCELLED, $source->status);
        $this->assertSame('0.00', $source->total);
        $this->assertSame('500.00', $target->total);
        $this->assertSame(2, $target->items()->count());
        $this->assertSame(1, $target->items()->where('client_line_id', 'OPS-MOVED-LINE')->count());
        $this->assertSame(DiningTable::STATUS_AVAILABLE, $table->fresh()->status);
        $this->assertSame(DiningTable::STATUS_OCCUPIED, $tableTwo->fresh()->status);
        $this->assertSame(DiningTable::STATUS_AVAILABLE, $tableThree->fresh()->status);

        tenancy()->end();
    }

    public function test_desktop_cancel_guest_updates_and_table_override_keep_cloud_occupancy_accurate(): void
    {
        [$business, $domain, $tenant] = $this->createActiveBusiness();
        $credentials = $this->activateDevice($business, $domain, 'desktop-occupancy-device');

        tenancy()->initialize($tenant);
        [$waiter, $table] = $this->seedRestaurant('waiter-occupancy@restaurant.test');
        $actorPublicId = $waiter->public_id;
        tenancy()->end();

        $token = $this->login($domain, 'waiter-occupancy@restaurant.test');
        $headers = $this->syncHeaders($token, $credentials);
        $push = fn (array $mutation) => $this->withHeaders($headers)
            ->postJson("http://{$domain}/api/v1/desktop/reconcile/push", ['mutations' => [$mutation]]);

        $mutation = fn (string $id, string $operation, string $localId, array $payload) => [
            'mutation_id' => $id,
            'operation' => $operation,
            'entity_type' => $operation === 'table.mark_available' ? 'dining_table' : 'order',
            'local_entity_id' => $localId,
            'actor_public_id' => $actorPublicId,
            'occurred_at' => now()->utc()->toIso8601String(),
            'payload' => $payload,
        ];

        $push($mutation('OCC-OPEN-1', 'order.open', 'occ-local-order-1', [
            'client_order_id' => 'OCC-1', 'dining_table_id' => $table->id, 'guest_count' => 1,
        ]))->assertOk()->assertJsonPath('data.results.0.status', 'accepted');

        $push($mutation('OCC-GUESTS-1', 'order.guests.update', 'occ-local-order-1', [
            'client_order_id' => 'OCC-1', 'guest_count' => 4,
        ]))->assertOk()->assertJsonPath('data.results.0.status', 'accepted');

        // A legacy second unsettled order on the same table (created before
        // this guard existed) must keep the table occupied after one cancel.
        tenancy()->initialize($tenant);
        Order::query()->create([
            'client_order_id' => 'OCC-2',
            'branch_id' => DiningArea::query()->whereKey($table->dining_area_id)->value('branch_id'),
            'dining_table_id' => $table->id,
            'waiter_id' => $waiter->id,
            'service_type' => Order::SERVICE_DINE_IN,
            'status' => Order::STATUS_DRAFT,
            'guest_count' => 2,
            'subtotal' => '0.00',
            'total' => '0.00',
            'opened_at' => now(),
        ]);
        tenancy()->end();

        $push($mutation('OCC-CANCEL-1', 'order.cancel', 'occ-local-order-1', [
            'client_order_id' => 'OCC-1', 'reason' => 'Guest left without ordering',
        ]))->assertOk()->assertJsonPath('data.results.0.status', 'accepted');

        tenancy()->initialize($tenant);
        $cancelled = Order::query()->where('client_order_id', 'OCC-1')->sole();
        $this->assertSame(Order::STATUS_CANCELLED, $cancelled->status);
        $this->assertSame(4, (int) $cancelled->guest_count);
        $this->assertSame(DiningTable::STATUS_OCCUPIED, $table->fresh()->status);
        tenancy()->end();

        // Unpaid remaining order blocks the manual override.
        $push($mutation('OCC-OVERRIDE-1', 'table.mark_available', $table->id, [
            'reason' => 'Stale occupancy',
        ]))->assertOk()->assertJsonPath('data.results.0.status', 'conflict');

        $push($mutation('OCC-CANCEL-2', 'order.cancel', 'occ-local-order-2', [
            'client_order_id' => 'OCC-2', 'reason' => 'Party finished',
        ]))->assertOk()->assertJsonPath('data.results.0.status', 'accepted');

        tenancy()->initialize($tenant);
        $this->assertSame(DiningTable::STATUS_AVAILABLE, $table->fresh()->status);

        $manager = TenantUser::query()->create([
            'public_id' => (string) Str::ulid(),
            'name' => 'Floor Manager',
            'email' => 'manager-occupancy@restaurant.test',
            'password' => 'password123',
            'is_active' => true,
            'role' => 'manager',
        ]);
        $managerPublicId = $manager->public_id;
        // Simulate stale occupancy: occupied with no active order.
        $table->update(['status' => DiningTable::STATUS_OCCUPIED]);
        tenancy()->end();

        $managerHeaders = $this->syncHeaders(
            $this->login($domain, 'manager-occupancy@restaurant.test'),
            $credentials,
        );

        $this->withHeaders($managerHeaders)
            ->postJson("http://{$domain}/api/v1/desktop/reconcile/push", ['mutations' => [[
                'mutation_id' => 'OCC-OVERRIDE-2',
                'operation' => 'table.mark_available',
                'entity_type' => 'dining_table',
                'local_entity_id' => $table->id,
                'actor_public_id' => $managerPublicId,
                'occurred_at' => now()->utc()->toIso8601String(),
                'payload' => ['reason' => 'Stale occupancy confirmed'],
            ]]])
            ->assertOk()
            ->assertJsonPath('data.results.0.status', 'accepted');

        tenancy()->initialize($tenant);
        $this->assertSame(DiningTable::STATUS_AVAILABLE, $table->fresh()->status);
        $audit = DesktopOperationalRecord::query()
            ->where('record_type', 'table.availability_override')
            ->latest('occurred_at')
            ->firstOrFail();
        $this->assertSame('Stale occupancy confirmed', $audit->payload['reason']);
        $this->assertSame('occupied', $audit->payload['previous_status']);
        tenancy()->end();
    }

    public function test_desktop_transfer_merge_split_and_void_reconcile_to_the_tenant(): void
    {
        [$business, $domain, $tenant] = $this->createActiveBusiness();
        $credentials = $this->activateDevice($business, $domain, 'desktop-cashier-ops-device');

        tenancy()->initialize($tenant);
        [$waiter, $table, $menuItem] = $this->seedRestaurant('waiter-cashier-ops@restaurant.test');
        $extraTables = [];
        foreach (['T-02', 'T-03', 'T-04', 'T-05'] as $code) {
            $extraTables[$code] = DiningTable::query()->create([
                'dining_area_id' => $table->dining_area_id,
                'code' => $code,
                'name' => $code,
                'capacity' => 4,
                'status' => DiningTable::STATUS_AVAILABLE,
                'is_active' => true,
            ]);
        }
        $actorPublicId = $waiter->public_id;
        tenancy()->end();

        $headers = $this->syncHeaders($this->login($domain, 'waiter-cashier-ops@restaurant.test'), $credentials);
        $push = fn (array $mutation) => $this->withHeaders($headers)
            ->postJson("http://{$domain}/api/v1/desktop/reconcile/push", ['mutations' => [$mutation]]);
        $accepted = function ($response): array {
            $result = $response->assertOk()->json('data.results.0');
            $this->assertSame('accepted', $result['status'] ?? '', (string) ($result['message'] ?? ''));

            return $result;
        };
        $mutation = fn (string $id, string $operation, string $localId, array $payload) => [
            'mutation_id' => $id,
            'operation' => $operation,
            'entity_type' => 'order',
            'local_entity_id' => $localId,
            'actor_public_id' => $actorPublicId,
            'occurred_at' => now()->utc()->toIso8601String(),
            'payload' => $payload,
        ];

        // Order 1 on T-01 with one line, then transfer to T-02.
        $accepted($push($mutation('B8-OPEN-1', 'order.open', 'b8-o1', [
            'client_order_id' => 'B8-1', 'dining_table_id' => $table->id, 'guest_count' => 2,
        ])));
        $accepted($push($mutation('B8-ADD-1', 'order.item.add', 'b8-l1', [
            'client_order_id' => 'B8-1', 'menu_item_id' => $menuItem->id,
            'quantity' => 1, 'client_line_id' => 'B8-L1',
        ])));
        $accepted($push($mutation('B8-TRANSFER-1', 'order.table.transfer', 'b8-o1', [
            'client_order_id' => 'B8-1',
            'target_table_id' => $extraTables['T-02']->id,
            'local_target_table_id' => $extraTables['T-02']->id,
        ])));

        tenancy()->initialize($tenant);
        $this->assertSame(
            $extraTables['T-02']->id,
            Order::query()->where('client_order_id', 'B8-1')->value('dining_table_id'),
        );
        $this->assertSame(DiningTable::STATUS_AVAILABLE, $table->fresh()->status);
        $this->assertSame(DiningTable::STATUS_OCCUPIED, $extraTables['T-02']->fresh()->status);
        tenancy()->end();

        // Order 2 on T-03 absorbs the line; the emptied source closes and frees T-02.
        $accepted($push($mutation('B8-OPEN-2', 'order.open', 'b8-o2', [
            'client_order_id' => 'B8-2', 'dining_table_id' => $extraTables['T-03']->id, 'guest_count' => 1,
        ])));
        $accepted($push($mutation('B8-MOVE-1', 'order.item.move', 'b8-o2', [
            'source_order_id' => 'B8-1',
            'target_order_id' => 'B8-2',
            'lines' => [[
                'client_line_id' => 'B8-L1',
                'target_client_line_id' => 'B8-2-B8-L1',
                'quantity' => 1,
            ]],
        ])));

        tenancy()->initialize($tenant);
        $this->assertSame('250.00', Order::query()->where('client_order_id', 'B8-2')->value('total'));
        $this->assertSame(Order::STATUS_CLOSED, Order::query()->where('client_order_id', 'B8-1')->value('status'));
        $this->assertSame(DiningTable::STATUS_AVAILABLE, $extraTables['T-02']->fresh()->status);
        tenancy()->end();

        // Split the moved line onto T-04 under a new order.
        $accepted($push($mutation('B8-SPLIT-1', 'order.split', 'b8-o3', [
            'source_order_id' => 'B8-2',
            'target_order_id' => 'B8-3',
            'target_table_id' => $extraTables['T-04']->id,
            'local_target_table_id' => $extraTables['T-04']->id,
            'lines' => [[
                'client_line_id' => 'B8-2-B8-L1',
                'target_client_line_id' => 'B8-3-B8-2-B8-L1',
                'quantity' => 1,
            ]],
        ])));

        tenancy()->initialize($tenant);
        $split = Order::query()->where('client_order_id', 'B8-3')->sole();
        $this->assertSame('250.00', $split->total);
        $this->assertSame(1, $split->items()->count());
        $this->assertSame(DiningTable::STATUS_OCCUPIED, $extraTables['T-04']->fresh()->status);
        $this->assertSame(Order::STATUS_CLOSED, Order::query()->where('client_order_id', 'B8-2')->value('status'));
        tenancy()->end();

        // Voiding the split line removes it from cloud totals.
        $accepted($push($mutation('B8-VOID-1', 'order.item.void', 'b8-l-split', [
            'client_order_id' => 'B8-3',
            'client_line_id' => 'B8-3-B8-2-B8-L1',
            'reason' => 'Guest changed their mind',
        ])));

        tenancy()->initialize($tenant);
        $split = Order::query()->where('client_order_id', 'B8-3')->sole();
        $this->assertSame('0.00', $split->total);
        $this->assertSame('voided', $split->items()->sole()->status);
        tenancy()->end();

        // Merge: an active order on T-01 folds into an order on T-03.
        $accepted($push($mutation('B8-OPEN-3', 'order.open', 'b8-o4', [
            'client_order_id' => 'B8-4', 'dining_table_id' => $table->id, 'guest_count' => 2,
        ])));
        $accepted($push($mutation('B8-ADD-2', 'order.item.add', 'b8-l4', [
            'client_order_id' => 'B8-4', 'menu_item_id' => $menuItem->id,
            'quantity' => 2, 'client_line_id' => 'B8-L4',
        ])));
        $accepted($push($mutation('B8-OPEN-4', 'order.open', 'b8-o5', [
            'client_order_id' => 'B8-5', 'dining_table_id' => $extraTables['T-03']->id, 'guest_count' => 1,
        ])));
        $accepted($push($mutation('B8-ADD-3', 'order.item.add', 'b8-l5', [
            'client_order_id' => 'B8-5', 'menu_item_id' => $menuItem->id,
            'quantity' => 1, 'client_line_id' => 'B8-L5',
        ])));
        $accepted($push($mutation('B8-MERGE-1', 'order.merge', 'b8-o5', [
            'source_order_id' => 'B8-4',
            'target_order_id' => 'B8-5',
        ])));

        tenancy()->initialize($tenant);
        $target = Order::query()->where('client_order_id', 'B8-5')->sole();
        $this->assertSame('750.00', $target->total);
        $this->assertSame(2, $target->items()->count());
        $this->assertSame(Order::STATUS_CANCELLED, Order::query()->where('client_order_id', 'B8-4')->value('status'));
        $this->assertSame(DiningTable::STATUS_AVAILABLE, $table->fresh()->status);
        $this->assertSame(DiningTable::STATUS_OCCUPIED, $extraTables['T-03']->fresh()->status);
        tenancy()->end();
    }

    public function test_sync_requires_valid_activated_device_secret_in_addition_to_user_token(): void
    {
        [$business, $domain, $tenant] = $this->createActiveBusiness();
        $credentials = $this->activateDevice($business, $domain, 'sync-device-004');

        tenancy()->initialize($tenant);
        $this->seedRestaurant('waiter1@restaurant.test');
        tenancy()->end();

        $token = $this->login($domain, 'waiter1@restaurant.test');

        $this->withHeaders([
            'Authorization' => 'Bearer '.$token,
            'X-Device-Id' => $credentials['device_id'],
            'X-Device-Secret' => 'wrong-secret',
        ])->getJson("http://{$domain}/api/v1/sync/bootstrap")
            ->assertUnprocessable()
            ->assertJsonValidationErrors('device_secret');
    }

    public function test_desktop_reconciliation_is_idempotent_links_local_ids_and_exposes_pull_cursor(): void
    {
        [$business, $domain, $tenant] = $this->createActiveBusiness();
        $credentials = $this->activateDevice($business, $domain, 'desktop-reconcile-device');

        tenancy()->initialize($tenant);
        [$waiter, $table] = $this->seedRestaurant('waiter1@restaurant.test');
        $actorPublicId = $waiter->public_id;
        tenancy()->end();

        $token = $this->login($domain, 'waiter1@restaurant.test');
        $headers = $this->syncHeaders($token, $credentials);

        $mutation = [
            'mutation_id' => 'DESKTOP-OPEN-001',
            'operation' => 'order.open',
            'entity_type' => 'order',
            'local_entity_id' => 'local-order-001',
            'actor_public_id' => $actorPublicId,
            'occurred_at' => now()->utc()->toIso8601String(),
            'payload' => [
                'client_order_id' => 'DESKTOP-CLIENT-001',
                'dining_table_id' => $table->id,
                'guest_count' => 3,
            ],
        ];

        $first = $this->withHeaders($headers)
            ->postJson("http://{$domain}/api/v1/desktop/reconcile/push", [
                'mutations' => [$mutation],
            ])
            ->assertOk()
            ->assertJsonPath('data.results.0.status', 'accepted')
            ->json('data');

        $retry = $this->withHeaders($headers)
            ->postJson("http://{$domain}/api/v1/desktop/reconcile/push", [
                'mutations' => [$mutation],
            ])
            ->assertOk()
            ->assertJsonPath('data.results.0.status', 'accepted')
            ->json('data');

        $this->assertSame(
            $first['results'][0]['entity_id'],
            $retry['results'][0]['entity_id'],
        );

        $pull = $this->withHeaders($headers)
            ->getJson("http://{$domain}/api/v1/desktop/reconcile/pull?cursor=0&limit=100")
            ->assertOk()
            ->json('data');

        $this->assertGreaterThan(0, $pull['cursor']);
        $this->assertContains('order', collect($pull['changes'])->pluck('entity_type')->all());

        tenancy()->initialize($tenant);

        $this->assertSame(1, Order::query()
            ->where('client_order_id', 'DESKTOP-CLIENT-001')
            ->count());

        $this->assertDatabaseHas('desktop_entity_links', [
            'entity_type' => 'order',
            'local_entity_id' => 'local-order-001',
            'cloud_entity_id' => $first['results'][0]['entity_id'],
        ], 'tenant');

        $this->assertSame(1, DesktopEntityLink::query()->count());

        tenancy()->end();
    }

    /**
     * @return array{Business, string, Tenant}
     */
    public function test_desktop_management_can_create_read_and_update_floor_table_category_menu_and_inventory(): void
    {
        [, $domain, $tenant] = $this->createActiveBusiness();

        tenancy()->initialize($tenant);
        [$owner, $table] = $this->seedRestaurant('management-owner@restaurant.test');
        $owner->update(['role' => 'owner']);
        $branchId = $table->diningArea->branch_id;
        tenancy()->end();

        $token = $this->login($domain, 'management-owner@restaurant.test');
        $this->withToken($token);

        $root = "http://{$domain}/api/v1/desktop/management";
        $this->getJson($root)->assertOk()
            ->assertJsonPath('data.branches.0.name', 'Main Branch');

        $areaId = $this->postJson($root.'/areas', [
            'branch_id' => $branchId,
            'name' => 'Upper Floor',
            'is_active' => true,
        ])->assertCreated()->json('data.id');

        $this->patchJson($root.'/areas/'.$areaId, [
            'branch_id' => $branchId,
            'name' => 'First Floor',
            'is_active' => true,
        ])->assertOk()->assertJsonPath('data.name', 'First Floor');

        $newTableId = $this->postJson($root.'/tables', [
            'dining_area_id' => $areaId,
            'code' => 'T-02',
            'name' => 'Table 2',
            'capacity' => 4,
            'is_active' => true,
        ])->assertCreated()->json('data.id');

        $this->patchJson($root.'/tables/'.$newTableId, [
            'dining_area_id' => $areaId,
            'code' => 'T-02',
            'name' => 'Window Table',
            'capacity' => 6,
            'is_active' => true,
        ])->assertOk()->assertJsonPath('data.name', 'Window Table');

        $categoryId = $this->postJson($root.'/categories', [
            'name' => 'Desserts',
            'is_active' => true,
        ])->assertCreated()->json('data.id');
        $this->patchJson($root.'/categories/'.$categoryId, [
            'name' => 'Desserts & Drinks',
            'is_active' => true,
        ])->assertOk()->assertJsonPath('data.name', 'Desserts & Drinks');

        $itemId = $this->postJson($root.'/menu-items', [
            'menu_category_id' => $categoryId,
            'sku' => 'DES-001',
            'name' => 'Ice Cream',
            'price' => 120,
            'is_available' => true,
        ])->assertCreated()->json('data.id');
        $this->patchJson($root.'/menu-items/'.$itemId, [
            'menu_category_id' => $categoryId,
            'sku' => 'DES-001',
            'name' => 'Vanilla Ice Cream',
            'price' => 130,
            'is_available' => true,
        ])->assertOk()->assertJsonPath('data.name', 'Vanilla Ice Cream');

        $stockId = $this->postJson($root.'/inventory', [
            'sku' => 'CREAM-001',
            'name' => 'Cream',
            'base_unit' => 'g',
            'purchase_unit' => 'kg',
            'purchase_to_base_factor' => 1000,
            'reorder_level' => 200,
            'is_active' => true,
        ])->assertCreated()->json('data.id');
        $this->patchJson($root.'/inventory/'.$stockId, [
            'sku' => 'CREAM-001',
            'name' => 'Fresh Cream',
            'base_unit' => 'g',
            'purchase_unit' => 'kg',
            'purchase_to_base_factor' => 1000,
            'reorder_level' => 250,
            'is_active' => true,
        ])->assertOk()->assertJsonPath('data.name', 'Fresh Cream');

        $this->getJson($root)->assertOk()
            ->assertJsonCount(2, 'data.areas')
            ->assertJsonCount(2, 'data.tables')
            ->assertJsonCount(2, 'data.categories')
            ->assertJsonCount(2, 'data.menu_items')
            ->assertJsonCount(1, 'data.inventory_items');

    }

    private function createActiveBusiness(): array
    {
        Carbon::setTestNow(Carbon::parse('2026-10-01 09:00:00', 'Asia/Kabul'));

        $plan = Plan::create([
            'code' => 'sync-'.Str::lower(Str::random(8)),
            'name' => 'Sync Test Plan',
            'is_active' => true,
        ]);
        $plan->features()->create([
            'feature_key' => 'max_devices',
            'value' => ['value' => '5'],
        ]);

        $tenantId = 'sync-'.Str::lower(Str::random(10));
        $database = config('tenancy.database.prefix').$tenantId.config('tenancy.database.suffix');
        @unlink(database_path($database));

        $tenant = Tenant::create([
            'id' => $tenantId,
            'provisioning_state' => ProvisioningState::Ready->value,
        ]);
        $domain = $tenantId.'.test';
        $tenant->domains()->create(['domain' => $domain]);
        $this->tenantDatabases[] = $tenant->database()->getName();
        $this->tenantStoragePaths[] = storage_path(config('tenancy.filesystem.suffix_base').$tenantId);

        $business = Business::create([
            'tenant_id' => $tenant->id,
            'plan_id' => $plan->id,
            'name' => 'Sync Test Restaurant',
            'contact_name' => 'Owner',
            'phone' => '+93700000031',
            'status' => BusinessStatus::Provisioning,
            'provisioning_state' => ProvisioningState::Ready,
        ]);

        app(SubscriptionService::class)->startTrial($business);

        return [$business->fresh(), $domain, $tenant];
    }

    private function activateDevice(
        Business $business,
        string $domain,
        string $deviceUid,
        string $platform = 'android',
    ): array {
        $license = app(LicenseService::class)->generate(
            $business,
            AdminUser::factory()->create([
                'role' => PlatformRole::Operator,
                'is_active' => true,
            ]),
        );

        $activation = $this->postJson("http://{$domain}/api/v1/license/activate", [
            'license_key' => $license['raw_key'],
            'device_uid' => $deviceUid,
            'device_name' => $platform === 'windows'
                ? 'Restaurant Desktop'
                : 'Waiter Test Phone',
            'platform' => $platform,
            'app_version' => '1.0.0',
        ])->assertCreated();

        return [
            'device_id' => $activation->json('device.id'),
            'device_secret' => $activation->json('device_secret'),
        ];
    }

    /**
     * @return array{TenantUser, DiningTable, MenuItem}
     */
    private function seedRestaurant(string $email): array
    {
        $waiter = TenantUser::query()->create([
            'public_id' => (string) Str::ulid(),
            'name' => 'Waiter One',
            'email' => $email,
            'password' => 'password123',
            'is_active' => true,
            'role' => 'waiter',
        ]);

        $branch = RestaurantBranch::query()->create([
            'code' => 'MAIN',
            'name' => 'Main Branch',
            'is_active' => true,
        ]);

        $area = DiningArea::query()->create([
            'branch_id' => $branch->id,
            'name' => 'Main Hall',
            'sort_order' => 1,
            'is_active' => true,
        ]);

        $table = DiningTable::query()->create([
            'dining_area_id' => $area->id,
            'code' => 'T-01',
            'name' => 'Table 1',
            'capacity' => 4,
            'status' => DiningTable::STATUS_AVAILABLE,
            'is_active' => true,
        ]);

        $category = MenuCategory::query()->create([
            'name' => 'Main',
            'sort_order' => 1,
            'is_active' => true,
        ]);

        $menuItem = MenuItem::query()->create([
            'menu_category_id' => $category->id,
            'sku' => 'FOOD-001',
            'name' => 'Kabuli Pulao',
            'price' => '250.00',
            'is_available' => true,
            'sort_order' => 1,
        ]);

        return [$waiter, $table, $menuItem];
    }

    private function login(string $domain, string $email): string
    {
        return $this->postJson("http://{$domain}/api/v1/auth/login", [
            'email' => $email,
            'password' => 'password123',
            'device_name' => 'sync-test',
        ])->assertOk()->json('access_token');
    }

    private function syncHeaders(string $token, array $credentials): array
    {
        return [
            'Authorization' => 'Bearer '.$token,
            'X-Device-Id' => $credentials['device_id'],
            'X-Device-Secret' => $credentials['device_secret'],
            'X-App-Version' => '1.0.0',
        ];
    }
}

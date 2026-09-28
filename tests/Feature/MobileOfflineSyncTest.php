<?php

namespace Tests\Feature;

use App\Enums\BusinessStatus;
use App\Enums\PlatformRole;
use App\Enums\ProvisioningState;
use App\Models\AdminUser;
use App\Models\Business;
use App\Models\DiningArea;
use App\Models\DiningTable;
use App\Models\MenuCategory;
use App\Models\MenuItem;
use App\Models\Order;
use App\Models\Plan;
use App\Models\RestaurantBranch;
use App\Models\SyncMutation;
use App\Models\Tenant;
use App\Models\TenantUser;
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

    public function test_device_bound_bootstrap_push_retry_and_incremental_pull_are_resumable(): void
    {
        [$business, $domain, $tenant] = $this->createActiveBusiness();
        $credentials = $this->activateDevice($business, $domain, 'sync-device-001');

        tenancy()->initialize($tenant);
        [$waiter, $table, $menuItem] = $this->seedRestaurant('waiter1@restaurant.test');
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

    /**
     * @return array{Business, string, Tenant}
     */
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

    private function activateDevice(Business $business, string $domain, string $deviceUid): array
    {
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
            'device_name' => 'Waiter Test Phone',
            'platform' => 'android',
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

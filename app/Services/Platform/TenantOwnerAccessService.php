<?php

namespace App\Services\Platform;

use App\Enums\ProvisioningState;
use App\Models\Business;
use App\Models\DiningArea;
use App\Models\KitchenStation;
use App\Models\RestaurantBranch;
use App\Models\TenantUser;
use Illuminate\Support\Str;
use RuntimeException;

class TenantOwnerAccessService
{
    /**
     * @return array{email: string, password: string, domain: string|null}
     */
    public function reset(Business $business, ?string $email = null, ?string $temporaryPassword = null): array
    {
        $business->loadMissing('tenant.domains');

        if (! $business->tenant || $business->provisioning_state !== ProvisioningState::Ready) {
            throw new RuntimeException('Provision the restaurant before creating owner access.');
        }

        $email = Str::lower(trim((string) ($email ?: $business->email)));

        if ($email === '') {
            throw new RuntimeException('Add an owner email address before creating owner access.');
        }

        $password = $temporaryPassword ?: Str::random(14);
        $domain = $business->tenant->domains->first()?->domain;

        if ($business->email !== $email) {
            $business->forceFill(['email' => $email])->save();
        }

        tenancy()->initialize($business->tenant);

        try {
            $owner = TenantUser::query()->where('role', 'owner')->first();

            if (! $owner) {
                $owner = TenantUser::query()->where('email', $email)->first();
            }

            if (! $owner) {
                $owner = new TenantUser;
                $owner->public_id = (string) Str::ulid();
            }

            $owner->email = $email;
            $owner->name = $business->contact_name ?: $business->name.' Owner';
            $owner->phone = $business->phone;
            $owner->role = 'owner';
            $owner->is_active = true;
            $owner->password = $password;
            $owner->save();

            $branch = RestaurantBranch::query()->firstOrCreate(
                ['code' => 'MAIN'],
                ['name' => $business->name, 'is_active' => true],
            );

            DiningArea::query()->firstOrCreate(
                ['branch_id' => $branch->id, 'name' => 'Main Hall'],
                ['sort_order' => 0, 'is_active' => true],
            );

            KitchenStation::query()->firstOrCreate(
                ['branch_id' => $branch->id, 'code' => 'MAIN'],
                ['name' => 'Main Kitchen', 'sort_order' => 0, 'is_active' => true],
            );
        } finally {
            tenancy()->end();
        }

        return [
            'email' => $email,
            'password' => $password,
            'domain' => $domain,
        ];
    }
}

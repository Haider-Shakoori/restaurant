<?php

namespace App\Services\Platform;

use App\Enums\ProvisioningState;
use App\Models\Business;
use App\Models\Tenant;
use Illuminate\Support\Str;
use RuntimeException;
use Throwable;

class BusinessProvisioningService
{
    public function provision(Business $business): Business
    {
        if (! $business->plan_id) {
            throw new RuntimeException('Assign a plan before provisioning this restaurant.');
        }

        if (! $business->requested_subdomain) {
            throw new RuntimeException('Set a restaurant subdomain before provisioning.');
        }

        if ($business->provisioning_state === ProvisioningState::Ready && $business->tenant_id) {
            return $business->fresh(['tenant.domains']);
        }

        $domain = $this->domainFor($business);
        $tenant = $business->tenant;

        try {
            $this->mark($business, ProvisioningState::Database, 'provisioning.started', 'Tenant database provisioning started.');

            if (! $tenant) {
                $tenant = Tenant::create([
                    'id' => Str::lower((string) Str::ulid()),
                    'provisioning_state' => ProvisioningState::Migrating->value,
                ]);

                $business->forceFill(['tenant_id' => $tenant->id])->save();
                $this->event($business, ProvisioningState::Migrating, 'tenant.created', 'Tenant database created and tenant migrations completed.');
            }

            $existingDomain = $tenant->domains()->where('domain', $domain)->first();

            if (! $existingDomain) {
                $tenant->domains()->create(['domain' => $domain]);
            }

            $this->event($business, ProvisioningState::Domain, 'domain.attached', 'Tenant domain attached: '.$domain);

            $tenant->forceFill(['provisioning_state' => ProvisioningState::Ready->value])->save();

            $business->forceFill([
                'provisioning_state' => ProvisioningState::Ready,
                'provisioning_error' => null,
            ])->save();

            $this->event($business, ProvisioningState::Ready, 'provisioning.ready', 'Restaurant tenant provisioning completed successfully.');

            return $business->fresh(['tenant.domains']);
        } catch (Throwable $e) {
            $business->forceFill([
                'provisioning_state' => ProvisioningState::Failed,
                'provisioning_error' => Str::limit($e->getMessage(), 1000),
            ])->save();

            $this->event($business, ProvisioningState::Failed, 'provisioning.failed', Str::limit($e->getMessage(), 1000));

            throw $e;
        }
    }

    private function domainFor(Business $business): string
    {
        $suffix = trim((string) config('platform.provisioning.tenant_domain_suffix'));

        if ($suffix === '') {
            throw new RuntimeException('Tenant domain suffix is not configured.');
        }

        return Str::lower($business->requested_subdomain.'.'.ltrim($suffix, '.'));
    }

    private function mark(Business $business, ProvisioningState $state, string $event, string $message): void
    {
        $business->forceFill([
            'provisioning_state' => $state,
            'provisioning_error' => null,
        ])->save();

        $this->event($business, $state, $event, $message);
    }

    private function event(Business $business, ProvisioningState $state, string $event, string $message): void
    {
        $business->provisioningEvents()->create([
            'event' => $event,
            'state' => $state->value,
            'message' => $message,
            'occurred_at' => now(),
        ]);
    }
}

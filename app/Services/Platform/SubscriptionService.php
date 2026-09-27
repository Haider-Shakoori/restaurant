<?php

namespace App\Services\Platform;

use App\Enums\BillingCycle;
use App\Enums\BusinessStatus;
use App\Enums\ProvisioningState;
use App\Enums\SubscriptionSource;
use App\Enums\SubscriptionStatus;
use App\Models\AdminUser;
use App\Models\Business;
use App\Models\PlanPrice;
use App\Models\Subscription;
use Illuminate\Support\Carbon;
use Illuminate\Support\Facades\DB;
use Illuminate\Validation\ValidationException;

class SubscriptionService
{
    public function startTrial(Business $business, ?AdminUser $admin = null): Subscription
    {
        $business->refresh()->loadMissing('plan.features');

        if (! $business->tenant_id || $business->provisioning_state !== ProvisioningState::Ready) {
            throw ValidationException::withMessages([
                'trial' => 'The restaurant must be fully provisioned before its trial can start.',
            ]);
        }

        if (! $business->plan || ! $business->plan->is_active) {
            throw ValidationException::withMessages([
                'plan' => 'Assign an active plan before starting the trial.',
            ]);
        }

        if ($business->subscriptions()->where('source', SubscriptionSource::Trial)->exists()) {
            throw ValidationException::withMessages([
                'trial' => 'This restaurant has already used its hosted trial.',
            ]);
        }

        return $this->centralTransaction(function () use ($business, $admin): Subscription {
            $startsAt = now();
            $endsAt = $startsAt->copy()->addDays((int) config('platform.trial_days', 7));

            $subscription = $business->subscriptions()->create([
                'plan_id' => $business->plan->id,
                'created_by_admin_id' => $admin?->id,
                'source' => SubscriptionSource::Trial,
                'status' => SubscriptionStatus::Trial,
                'price_snapshot' => 0,
                'currency' => 'AFN',
                'plan_code_snapshot' => $business->plan->code,
                'plan_name_snapshot' => $business->plan->name,
                'features_snapshot' => $business->plan->entitlementSnapshot(),
                'starts_at' => $startsAt,
                'ends_at' => $endsAt,
                'activated_at' => $startsAt,
            ]);

            $business->update([
                'status' => BusinessStatus::Trial,
                'trial_starts_at' => $startsAt,
                'trial_ends_at' => $endsAt,
                'subscription_ends_at' => $endsAt,
                'first_activated_at' => $business->first_activated_at ?? $startsAt,
            ]);

            $this->recordEvent(
                $business,
                $subscription,
                $admin,
                'trial.started',
                SubscriptionStatus::Trial,
                'Seven-day hosted trial started after successful provisioning.',
                ['trial_days' => (int) config('platform.trial_days', 7)],
            );

            return $subscription;
        });
    }

    public function renew(
        Business $business,
        PlanPrice $price,
        AdminUser $admin,
        ?int $customDays = null,
    ): Subscription {
        $business->refresh()->loadMissing('plan.features');

        if (! $business->tenant_id || $business->provisioning_state !== ProvisioningState::Ready) {
            throw ValidationException::withMessages([
                'subscription' => 'The restaurant must be fully provisioned before renewal.',
            ]);
        }

        if (! $business->plan || $price->plan_id !== $business->plan_id) {
            throw ValidationException::withMessages([
                'plan_price_id' => 'The selected price does not belong to the restaurant plan.',
            ]);
        }

        if (! $business->plan->is_active || ! $price->is_active) {
            throw ValidationException::withMessages([
                'plan_price_id' => 'The selected plan price is inactive.',
            ]);
        }

        if ($price->billing_cycle === BillingCycle::Custom && (! $customDays || $customDays < 1)) {
            throw ValidationException::withMessages([
                'custom_days' => 'A positive custom duration is required for custom billing.',
            ]);
        }

        return $this->centralTransaction(function () use ($business, $price, $admin, $customDays): Subscription {
            $latestEnd = $business->subscriptions()
                ->whereNull('cancelled_at')
                ->max('ends_at');

            $startsAt = $latestEnd && Carbon::parse($latestEnd)->isFuture()
                ? Carbon::parse($latestEnd)
                : now();

            $endsAt = $this->calculateEnd($startsAt->copy(), $price, $customDays);

            $subscription = $business->subscriptions()->create([
                'plan_id' => $business->plan->id,
                'plan_price_id' => $price->id,
                'created_by_admin_id' => $admin->id,
                'source' => SubscriptionSource::ManualRenewal,
                'status' => $startsAt->isFuture() ? SubscriptionStatus::Scheduled : SubscriptionStatus::Active,
                'billing_cycle' => $price->billing_cycle,
                'interval_months' => $price->interval_months,
                'custom_days' => $customDays,
                'price_snapshot' => $price->price,
                'currency' => $price->currency,
                'plan_code_snapshot' => $business->plan->code,
                'plan_name_snapshot' => $business->plan->name,
                'features_snapshot' => $business->plan->entitlementSnapshot(),
                'starts_at' => $startsAt,
                'ends_at' => $endsAt,
                'activated_at' => $startsAt->isFuture() ? null : now(),
            ]);

            $business->update([
                'subscription_ends_at' => $endsAt,
                'first_activated_at' => $business->first_activated_at ?? now(),
            ]);

            $this->syncBusinessStatus($business);

            $this->recordEvent(
                $business,
                $subscription,
                $admin,
                'subscription.renewed',
                $subscription->status,
                $startsAt->isFuture()
                    ? 'Subscription renewed early and scheduled after the current period.'
                    : 'Subscription renewed from the current server time.',
                [
                    'billing_cycle' => $price->billing_cycle->value,
                    'custom_days' => $customDays,
                    'price' => $price->price,
                    'currency' => $price->currency,
                ],
            );

            return $subscription;
        });
    }

    public function cancelAccess(Business $business, AdminUser $admin): int
    {
        return $this->centralTransaction(function () use ($business, $admin): int {
            $subscriptions = $business->subscriptions()
                ->whereNull('cancelled_at')
                ->where('ends_at', '>', now())
                ->get();

            foreach ($subscriptions as $subscription) {
                $subscription->update([
                    'status' => SubscriptionStatus::Cancelled,
                    'cancelled_at' => now(),
                ]);
            }

            $business->update([
                'status' => BusinessStatus::Cancelled,
                'subscription_ends_at' => now(),
            ]);

            $this->recordEvent(
                $business,
                $subscriptions->first(),
                $admin,
                'subscription.cancelled',
                SubscriptionStatus::Cancelled,
                'Current and scheduled access periods were cancelled without deleting restaurant data.',
                ['cancelled_periods' => $subscriptions->count()],
            );

            return $subscriptions->count();
        });
    }

    public function access(Business $business): SubscriptionAccess
    {
        $business->loadMissing('subscriptions');

        if (! $business->tenant_id || $business->provisioning_state !== ProvisioningState::Ready) {
            return new SubscriptionAccess(false, 'provisioning', 'subscription_not_ready');
        }

        $now = now();

        $current = $business->subscriptions()
            ->whereNull('cancelled_at')
            ->where('starts_at', '<=', $now)
            ->where('ends_at', '>', $now)
            ->orderByDesc('starts_at')
            ->first();

        if ($current) {
            $status = $current->source === SubscriptionSource::Trial ? 'trial' : 'active';

            return new SubscriptionAccess(
                true,
                $status,
                'ok',
                $current,
                $current->ends_at,
                $current->features_snapshot ?? [],
            );
        }

        $future = $business->subscriptions()
            ->whereNull('cancelled_at')
            ->where('starts_at', '>', $now)
            ->orderBy('starts_at')
            ->first();

        if ($future) {
            return new SubscriptionAccess(
                false,
                'scheduled',
                'subscription_scheduled',
                $future,
                $future->ends_at,
                $future->features_snapshot ?? [],
            );
        }

        if ($business->status === BusinessStatus::Cancelled) {
            return new SubscriptionAccess(false, 'cancelled', 'subscription_cancelled');
        }

        $latest = $business->subscriptions()
            ->whereNull('cancelled_at')
            ->orderByDesc('ends_at')
            ->first();

        if (! $latest) {
            return new SubscriptionAccess(false, 'required', 'subscription_required');
        }

        $dueDays = max(0, (int) config('platform.subscription.due_days', 0));
        $dueUntil = $latest->ends_at?->copy()->addDays($dueDays);

        if ($dueDays > 0 && $dueUntil?->isFuture()) {
            return new SubscriptionAccess(
                false,
                'due',
                'subscription_due',
                $latest,
                $latest->ends_at,
                $latest->features_snapshot ?? [],
            );
        }

        return new SubscriptionAccess(
            false,
            'expired',
            'subscription_expired',
            $latest,
            $latest->ends_at,
            $latest->features_snapshot ?? [],
        );
    }

    public function syncBusinessStatus(Business $business): BusinessStatus
    {
        $this->refreshSubscriptionRecordStates($business);

        $access = $this->access($business);

        $status = match ($access->status) {
            'trial' => BusinessStatus::Trial,
            'active', 'scheduled' => BusinessStatus::Active,
            'due' => BusinessStatus::Due,
            'provisioning' => BusinessStatus::Provisioning,
            default => $business->status === BusinessStatus::Cancelled
                ? BusinessStatus::Cancelled
                : BusinessStatus::Expired,
        };

        $business->update(['status' => $status]);

        return $status;
    }

    public function featureValue(Business $business, string $feature, mixed $default = null): mixed
    {
        $access = $this->access($business);

        if (! $access->allowed) {
            return $default;
        }

        return $access->feature($feature, $default);
    }

    public function featureEnabled(Business $business, string $feature): bool
    {
        $value = $this->featureValue($business, $feature, false);

        return ! in_array($value, [false, null, 0, '0', '', 'false'], true);
    }

    public function refreshLifecycleStates(): int
    {
        $updated = 0;

        Business::query()
            ->whereNotNull('tenant_id')
            ->chunkById(100, function ($businesses) use (&$updated): void {
                foreach ($businesses as $business) {
                    $before = $business->status;
                    $after = $this->syncBusinessStatus($business);

                    if ($before !== $after) {
                        $updated++;
                    }
                }
            }, 'id');

        return $updated;
    }

    private function refreshSubscriptionRecordStates(Business $business): void
    {
        $now = now();

        $business->subscriptions()
            ->whereNull('cancelled_at')
            ->where('ends_at', '<=', $now)
            ->where('status', '!=', SubscriptionStatus::Expired)
            ->update(['status' => SubscriptionStatus::Expired->value]);

        $business->subscriptions()
            ->whereNull('cancelled_at')
            ->where('source', SubscriptionSource::ManualRenewal)
            ->where('starts_at', '<=', $now)
            ->where('ends_at', '>', $now)
            ->where('status', SubscriptionStatus::Scheduled)
            ->update([
                'status' => SubscriptionStatus::Active->value,
                'activated_at' => $now,
            ]);
    }

    private function calculateEnd(Carbon $startsAt, PlanPrice $price, ?int $customDays): Carbon
    {
        if ($price->billing_cycle === BillingCycle::Custom) {
            return $startsAt->addDays((int) $customDays);
        }

        $months = $price->interval_months ?? $price->billing_cycle->intervalMonths();

        if (! $months) {
            throw ValidationException::withMessages([
                'plan_price_id' => 'The plan price has no valid billing duration.',
            ]);
        }

        return $startsAt->addMonthsNoOverflow($months);
    }

    private function recordEvent(
        Business $business,
        ?Subscription $subscription,
        ?AdminUser $admin,
        string $event,
        SubscriptionStatus $status,
        string $message,
        array $context = [],
    ): void {
        $business->subscriptionEvents()->create([
            'subscription_id' => $subscription?->id,
            'admin_user_id' => $admin?->id,
            'event' => $event,
            'status' => $status->value,
            'message' => $message,
            'context' => $context,
            'occurred_at' => now(),
        ]);
    }

    private function centralTransaction(callable $callback): mixed
    {
        return DB::connection(config('tenancy.database.central_connection'))->transaction($callback);
    }
}

<?php

namespace App\Http\Requests\Platform;

use App\Enums\BillingCycle;
use Illuminate\Foundation\Http\FormRequest;
use Illuminate\Validation\Rule;
use Illuminate\Validation\Rules\Enum;

class StorePlanPriceRequest extends FormRequest
{
    public function authorize(): bool
    {
        return $this->user()?->can('manage-platform') ?? false;
    }

    public function rules(): array
    {
        $plan = $this->route('plan');
        $planPrice = $this->route('planPrice');

        return [
            'billing_cycle' => [
                'required',
                new Enum(BillingCycle::class),
                Rule::unique('plan_prices', 'billing_cycle')
                    ->where(fn ($query) => $query->where('plan_id', $plan->id))
                    ->ignore($planPrice?->id),
            ],
            'price' => ['required', 'numeric', 'min:0', 'decimal:0,2'],
            'currency' => ['required', 'string', 'size:3', 'regex:/^[A-Z]{3}$/'],
            'is_active' => ['nullable', 'boolean'],
            'sort_order' => ['nullable', 'integer', 'min:0', 'max:9999'],
        ];
    }
}

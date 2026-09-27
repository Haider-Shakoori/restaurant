<?php

namespace App\Http\Requests\Platform;

use Illuminate\Foundation\Http\FormRequest;

class RenewSubscriptionRequest extends FormRequest
{
    public function authorize(): bool
    {
        return $this->user()?->can('manage-platform') ?? false;
    }

    public function rules(): array
    {
        return [
            'plan_price_id' => ['required', 'integer', 'exists:plan_prices,id'],
            'custom_days' => ['nullable', 'integer', 'min:1', 'max:3650'],
        ];
    }
}

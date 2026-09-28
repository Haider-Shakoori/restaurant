<?php

namespace App\Http\Requests\Tenant;

use Illuminate\Foundation\Http\FormRequest;
use Illuminate\Validation\Rule;

class StoreRestaurantOnboardingRequest extends FormRequest
{
    public function authorize(): bool
    {
        return auth('tenant')->check();
    }

    public function rules(): array
    {
        return [
            'name' => ['required', 'string', 'max:255'],
            'phone' => ['nullable', 'string', 'max:50'],
            'email' => ['nullable', 'email', 'max:255'],
            'address' => ['nullable', 'string', 'max:255'],
            'country_code' => ['required', 'regex:/^[A-Za-z]{2}$/'],
            'currency' => ['required', 'regex:/^[A-Za-z]{3}$/'],
            'timezone' => ['required', 'timezone'],
            'locale' => ['required', Rule::in(array_keys(config('restaurant.locales', [])))],
        ];
    }
}

<?php

namespace App\Http\Requests\Platform;

use Illuminate\Foundation\Http\FormRequest;
use Illuminate\Validation\Rule;

class StoreBusinessRequest extends FormRequest
{
    public function authorize(): bool
    {
        return $this->user()?->can('manage-platform') ?? false;
    }

    public function rules(): array
    {
        $business = $this->route('business');

        return [
            'name' => ['required', 'string', 'max:255'],
            'requested_subdomain' => [
                'nullable',
                'string',
                'max:100',
                'regex:/^[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?$/',
                Rule::notIn(config('platform.reserved_subdomains', [])),
                Rule::unique('businesses', 'requested_subdomain')->ignore($business?->id),
            ],
            'contact_name' => ['required', 'string', 'max:255'],
            'phone' => ['required', 'string', 'max:50'],
            'whatsapp' => ['nullable', 'string', 'max:50'],
            'email' => ['nullable', 'email', 'max:255'],
            'location' => ['nullable', 'string', 'max:255'],
            'plan_id' => ['nullable', 'exists:plans,id'],
            'assigned_operator_id' => ['nullable', 'exists:admin_users,id'],
        ];
    }
}

<?php

namespace App\Http\Requests\Public;

use Illuminate\Foundation\Http\FormRequest;
use Illuminate\Validation\Rule;

class StoreTrialRequest extends FormRequest
{
    public function authorize(): bool
    {
        return true;
    }

    public function rules(): array
    {
        return [
            'name' => ['required', 'string', 'max:255'],
            'requested_subdomain' => [
                'required',
                'string',
                'max:100',
                'regex:/^[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?$/',
                Rule::notIn(config('platform.reserved_subdomains', [])),
                Rule::unique('businesses', 'requested_subdomain'),
            ],
            'contact_name' => ['required', 'string', 'max:255'],
            'phone' => ['required', 'string', 'max:50'],
            'whatsapp' => ['nullable', 'string', 'max:50'],
            'email' => ['nullable', 'email', 'max:255'],
            'location' => ['required', 'string', 'max:255'],
            'plan_id' => [
                'nullable',
                Rule::exists('plans', 'id')->where(fn ($query) => $query->where('is_active', true)),
            ],
        ];
    }

    public function messages(): array
    {
        return [
            'requested_subdomain.regex' => 'Use only lowercase letters, numbers, and hyphens for the restaurant address.',
            'requested_subdomain.unique' => 'That restaurant address is already reserved. Please choose another.',
        ];
    }
}

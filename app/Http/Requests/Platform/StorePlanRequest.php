<?php

namespace App\Http\Requests\Platform;

use Illuminate\Foundation\Http\FormRequest;
use Illuminate\Validation\Rule;

class StorePlanRequest extends FormRequest
{
    public function authorize(): bool
    {
        return $this->user()?->can('manage-platform') ?? false;
    }

    public function rules(): array
    {
        $plan = $this->route('plan');

        return [
            'code' => [
                'required',
                'string',
                'max:64',
                'regex:/^[a-z0-9_-]+$/',
                Rule::unique('plans', 'code')->ignore($plan?->id),
            ],
            'name' => ['required', 'string', 'max:255'],
            'description' => ['nullable', 'string', 'max:1000'],
            'is_active' => ['nullable', 'boolean'],
            'sort_order' => ['nullable', 'integer', 'min:0', 'max:9999'],
            'features' => ['nullable', 'array'],
            'features.*.key' => ['required_with:features', 'string', 'max:100', 'distinct'],
            'features.*.value' => ['nullable', 'string', 'max:255'],
        ];
    }
}

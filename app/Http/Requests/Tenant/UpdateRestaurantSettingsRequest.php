<?php

namespace App\Http\Requests\Tenant;

use Illuminate\Foundation\Http\FormRequest;
use Illuminate\Validation\Rule;

class UpdateRestaurantSettingsRequest extends FormRequest
{
    public function authorize(): bool
    {
        return true;
    }

    public function rules(): array
    {
        return [
            'branch_id' => ['nullable', 'string', 'exists:branches,id'],
            'kitchen_queue_enabled' => ['required', 'boolean'],
            'preparing_stage_enabled' => ['required', 'boolean'],
            'expo_enabled' => ['required', 'boolean'],
            'courses_enabled' => ['required', 'boolean'],
            'kot_sound_enabled' => ['required', 'boolean'],
            'kitchen_warning_minutes' => ['required', 'integer', 'min:1', 'max:240'],
            'kitchen_late_minutes' => ['required', 'integer', 'min:1', 'max:480', 'gte:kitchen_warning_minutes'],
            'require_manager_approval_post_kot_void' => ['required', 'boolean'],
            'negative_stock_policy' => ['required', Rule::in(['block', 'warn', 'allow'])],
        ];
    }
}

<?php

namespace App\Http\Requests\Tenant;

use Illuminate\Foundation\Http\FormRequest;
use Illuminate\Validation\Rule;

class TakeOrderRequest extends FormRequest
{
    public function authorize(): bool
    {
        return true;
    }

    public function rules(): array
    {
        return [
            'dining_table_id' => ['required', 'string', 'max:40'],
            'guest_count' => ['required', 'integer', 'min:1', 'max:100'],
            'notes' => ['nullable', 'string', 'max:2000'],
            'submit_action' => ['required', Rule::in(['draft', 'kitchen'])],
            'client_dispatch_id' => ['nullable', 'string', 'max:80'],
            'lines' => ['required', 'array', 'min:1', 'max:100'],
            'lines.*.menu_item_id' => ['required', 'string', 'max:40'],
            'lines.*.quantity' => ['required', 'integer', 'min:1', 'max:999'],
            'lines.*.notes' => ['nullable', 'string', 'max:1000'],
        ];
    }
}

<?php

namespace App\Http\Requests\Tenant;

use App\Models\Order;
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
            'branch_id' => ['nullable', 'string', 'exists:branches,id'],
            'service_type' => ['required', Rule::in(Order::SERVICE_TYPES)],
            'service_reference' => ['nullable', 'string', 'max:120'],
            'dining_table_id' => [
                'nullable',
                'string',
                'max:40',
                'required_if:service_type,dine_in',
                'exists:dining_tables,id',
            ],
            'guest_count' => ['required', 'integer', 'min:1', 'max:100'],
            'notes' => ['nullable', 'string', 'max:2000'],
            'submit_action' => ['required', Rule::in(['draft', 'kitchen'])],
            'lines' => ['required', 'array', 'min:1', 'max:100'],
            'lines.*.menu_item_id' => ['required', 'string', 'max:40'],
            'lines.*.quantity' => ['required', 'integer', 'min:1', 'max:999'],
            'lines.*.notes' => ['nullable', 'string', 'max:1000'],
            'lines.*.seat_number' => ['nullable', 'integer', 'min:1', 'max:999'],
            'lines.*.course_number' => ['nullable', 'integer', 'min:1', 'max:99'],
            'lines.*.course_name' => ['nullable', 'string', 'max:80'],
            'lines.*.hold_for_course' => ['nullable', 'boolean'],
            'lines.*.modifiers' => ['nullable', 'array', 'max:50'],
            'lines.*.modifiers.*.option_id' => ['required', 'string', 'max:40'],
            'lines.*.allergy_instructions' => ['nullable', 'string', 'max:1000'],
            'lines.*.kitchen_instructions' => ['nullable', 'string', 'max:1000'],
        ];
    }
}

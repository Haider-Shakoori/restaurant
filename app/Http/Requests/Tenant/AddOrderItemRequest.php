<?php

namespace App\Http\Requests\Tenant;

use Illuminate\Foundation\Http\FormRequest;

class AddOrderItemRequest extends FormRequest
{
    public function authorize(): bool
    {
        return true;
    }

    public function rules(): array
    {
        return [
            'client_line_id' => ['nullable', 'string', 'max:40'],
            'menu_item_id' => ['required', 'string', 'max:40'],
            'quantity' => ['required', 'integer', 'min:1', 'max:999'],
            'notes' => ['nullable', 'string', 'max:1000'],
            'seat_number' => ['nullable', 'integer', 'min:1', 'max:999'],
            'course_number' => ['nullable', 'integer', 'min:1', 'max:99'],
            'course_name' => ['nullable', 'string', 'max:80'],
            'modifiers' => ['nullable', 'array', 'max:50'],
            'modifiers.*.option_id' => ['required', 'string', 'max:40'],
            'allergy_instructions' => ['nullable', 'string', 'max:1000'],
            'kitchen_instructions' => ['nullable', 'string', 'max:1000'],
        ];
    }
}

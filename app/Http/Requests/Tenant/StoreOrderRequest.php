<?php

namespace App\Http\Requests\Tenant;

use App\Models\Order;
use Illuminate\Foundation\Http\FormRequest;
use Illuminate\Validation\Rule;

class StoreOrderRequest extends FormRequest
{
    public function authorize(): bool
    {
        return true;
    }

    public function rules(): array
    {
        return [
            'client_order_id' => ['nullable', 'string', 'max:40'],
            'branch_id' => ['nullable', 'string', 'max:40', 'exists:branches,id'],
            'service_type' => ['nullable', Rule::in(Order::SERVICE_TYPES)],
            'service_reference' => ['nullable', 'string', 'max:120'],
            'dining_table_id' => ['nullable', 'string', 'max:40', 'required_if:service_type,dine_in'],
            'guest_count' => ['nullable', 'integer', 'min:1', 'max:100'],
            'notes' => ['nullable', 'string', 'max:2000'],
        ];
    }
}

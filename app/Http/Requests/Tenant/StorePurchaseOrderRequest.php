<?php

namespace App\Http\Requests\Tenant;

use Illuminate\Foundation\Http\FormRequest;

class StorePurchaseOrderRequest extends FormRequest
{
    public function authorize(): bool
    {
        return true;
    }

    public function rules(): array
    {
        return [
            'branch_id' => ['required', 'string', 'max:40'],
            'supplier_id' => ['required', 'string', 'max:40'],
            'notes' => ['nullable', 'string', 'max:2000'],
            'lines' => ['required', 'array', 'min:1', 'max:200'],
            'lines.*.inventory_item_id' => ['required', 'string', 'max:40'],
            'lines.*.purchase_quantity' => ['required', 'decimal:0,4', 'gt:0'],
            'lines.*.unit_cost' => ['required', 'decimal:0,2', 'min:0'],
        ];
    }
}

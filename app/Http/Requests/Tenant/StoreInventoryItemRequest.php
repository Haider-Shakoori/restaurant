<?php

namespace App\Http\Requests\Tenant;

use Illuminate\Foundation\Http\FormRequest;

class StoreInventoryItemRequest extends FormRequest
{
    public function authorize(): bool
    {
        return true;
    }

    public function rules(): array
    {
        return [
            'sku' => ['required', 'string', 'max:80'],
            'name' => ['required', 'string', 'max:255'],
            'base_unit' => ['required', 'string', 'max:24'],
            'purchase_unit' => ['nullable', 'string', 'max:24'],
            'purchase_to_base_factor' => ['nullable', 'decimal:0,6', 'gt:0'],
            'reorder_level' => ['nullable', 'decimal:0,4', 'min:0'],
        ];
    }
}

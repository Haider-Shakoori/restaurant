<?php

namespace App\Http\Requests\Tenant;

use Illuminate\Foundation\Http\FormRequest;

class StoreRecipeRequest extends FormRequest
{
    public function authorize(): bool
    {
        return true;
    }

    public function rules(): array
    {
        return [
            'branch_id' => ['required', 'string', 'max:40'],
            'name' => ['nullable', 'string', 'max:255'],
            'items' => ['required', 'array', 'min:1', 'max:100'],
            'items.*.inventory_item_id' => ['required', 'string', 'max:40'],
            'items.*.quantity_base' => ['required_without:items.*.quantity', 'decimal:0,4', 'gt:0'],
            'items.*.quantity' => ['sometimes', 'required', 'decimal:0,4', 'gt:0'],
            'items.*.unit' => ['required_with:items.*.quantity', 'string', 'in:kg,g,l,ml,pcs'],
        ];
    }
}

<?php

namespace App\Http\Requests\Tenant;

use Illuminate\Foundation\Http\FormRequest;

class AdjustInventoryRequest extends FormRequest
{
    public function authorize(): bool
    {
        return true;
    }

    public function rules(): array
    {
        return [
            'branch_id' => ['required', 'string', 'max:40'],
            'quantity_delta' => ['required', 'decimal:0,4', 'not_in:0,0.0,0.00,0.000,0.0000'],
            'client_adjustment_id' => ['required', 'string', 'max:80'],
            'reason' => ['required', 'string', 'max:1000'],
        ];
    }
}

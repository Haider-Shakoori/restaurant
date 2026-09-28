<?php

namespace App\Http\Requests\Tenant;

use Illuminate\Foundation\Http\FormRequest;

class ApplyBillDiscountRequest extends FormRequest
{
    public function authorize(): bool
    {
        return true;
    }

    public function rules(): array
    {
        return [
            'type' => ['required', 'in:fixed,percent'],
            'value' => ['required', 'decimal:0,2', 'min:0'],
            'reason' => ['nullable', 'string', 'max:500'],
        ];
    }
}

<?php

namespace App\Http\Requests\Tenant;

use Illuminate\Foundation\Http\FormRequest;

class StoreSupplierPaymentRequest extends FormRequest
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
            'payment_account_id' => ['required', 'string', 'max:40'],
            'client_supplier_payment_id' => ['nullable', 'string', 'max:80'],
            'amount' => ['required', 'decimal:0,2', 'gt:0'],
            'payment_date' => ['required', 'date_format:Y-m-d'],
            'reference' => ['nullable', 'string', 'max:255'],
        ];
    }
}

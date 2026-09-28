<?php

namespace App\Http\Requests\Tenant;

use Illuminate\Foundation\Http\FormRequest;

class StoreBillPaymentRequest extends FormRequest
{
    public function authorize(): bool
    {
        return true;
    }

    public function rules(): array
    {
        return [
            'cashier_session_id' => ['required', 'string', 'max:40'],
            'client_payment_id' => ['nullable', 'string', 'max:50'],
            'method' => ['required', 'in:cash,card,bank,mobile_money,other'],
            'amount' => ['required', 'decimal:0,2', 'gt:0'],
            'reference' => ['nullable', 'string', 'max:255'],
        ];
    }
}

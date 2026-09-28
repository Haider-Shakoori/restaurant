<?php

namespace App\Http\Requests\Tenant;

use Illuminate\Foundation\Http\FormRequest;

class StoreOperatingExpenseRequest extends FormRequest
{
    public function authorize(): bool
    {
        return true;
    }

    public function rules(): array
    {
        return [
            'branch_id' => ['required', 'string', 'max:40'],
            'expense_account_id' => ['required', 'string', 'max:40'],
            'payment_account_id' => ['required', 'string', 'max:40'],
            'client_expense_id' => ['nullable', 'string', 'max:80'],
            'description' => ['required', 'string', 'max:500'],
            'amount' => ['required', 'decimal:0,2', 'gt:0'],
            'expense_date' => ['required', 'date_format:Y-m-d'],
            'reference' => ['nullable', 'string', 'max:255'],
        ];
    }
}

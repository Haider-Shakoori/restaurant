<?php

namespace App\Http\Requests\Tenant;

use Illuminate\Foundation\Http\FormRequest;

class StoreManualJournalRequest extends FormRequest
{
    public function authorize(): bool
    {
        return true;
    }

    public function rules(): array
    {
        return [
            'branch_id' => ['nullable', 'string', 'max:40'],
            'client_journal_id' => ['required', 'string', 'max:80'],
            'description' => ['required', 'string', 'max:500'],
            'entry_date' => ['required', 'date_format:Y-m-d'],
            'lines' => ['required', 'array', 'min:2', 'max:100'],
            'lines.*.account_id' => ['required', 'string', 'max:40'],
            'lines.*.debit' => ['nullable', 'decimal:0,2', 'min:0'],
            'lines.*.credit' => ['nullable', 'decimal:0,2', 'min:0'],
            'lines.*.memo' => ['nullable', 'string', 'max:500'],
        ];
    }
}

<?php

namespace App\Http\Requests\Tenant;

use Illuminate\Foundation\Http\FormRequest;

class SyncPullRequest extends FormRequest
{
    public function authorize(): bool
    {
        return true;
    }

    public function rules(): array
    {
        return [
            'cursor' => ['nullable', 'integer', 'min:0'],
            'limit' => [
                'nullable',
                'integer',
                'min:1',
                'max:'.max(1, (int) config('restaurant.performance.sync_batch_size', 100)),
            ],
            'catalog_etag' => ['nullable', 'string', 'max:64'],
        ];
    }
}

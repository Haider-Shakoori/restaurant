<?php

namespace App\Http\Requests\Tenant;

use Illuminate\Foundation\Http\FormRequest;

class SyncPushRequest extends FormRequest
{
    public function authorize(): bool
    {
        return true;
    }

    public function rules(): array
    {
        $max = max(1, (int) config('restaurant.performance.sync_batch_size', 100));

        return [
            'batch_id' => ['required', 'string', 'max:80'],
            'mutations' => ['required', 'array', 'min:1', 'max:'.$max],
            'mutations.*.mutation_id' => ['required', 'string', 'max:80'],
            'mutations.*.operation' => ['required', 'string', 'max:64'],
            'mutations.*.occurred_at' => ['nullable', 'date'],
            'mutations.*.payload' => ['required', 'array'],
        ];
    }
}

<?php

namespace App\Http\Requests\Tenant;

use Illuminate\Foundation\Http\FormRequest;

class RefreshLeaseRequest extends FormRequest
{
    protected function prepareForValidation(): void
    {
        $this->merge([
            'device_id' => $this->header('X-Device-Id', $this->input('device_id')),
            'device_secret' => $this->header('X-Device-Secret', $this->input('device_secret')),
        ]);
    }

    public function authorize(): bool
    {
        return true;
    }

    public function rules(): array
    {
        return [
            'device_id' => ['required', 'string', 'max:64'],
            'device_secret' => ['required', 'string', 'max:128'],
            'app_version' => ['nullable', 'string', 'max:50'],
        ];
    }
}

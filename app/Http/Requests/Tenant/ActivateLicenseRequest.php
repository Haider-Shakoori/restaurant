<?php

namespace App\Http\Requests\Tenant;

use Illuminate\Foundation\Http\FormRequest;

class ActivateLicenseRequest extends FormRequest
{
    public function authorize(): bool
    {
        return true;
    }

    public function rules(): array
    {
        return [
            'license_key' => ['required', 'string', 'max:64'],
            'device_uid' => ['required', 'string', 'max:191'],
            'device_name' => ['nullable', 'string', 'max:255'],
            'platform' => ['required', 'in:android,ios,windows'],
            'app_version' => ['nullable', 'string', 'max:50'],
        ];
    }
}

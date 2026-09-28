<?php

namespace App\Http\Requests\Tenant;

use Illuminate\Foundation\Http\FormRequest;

class AssignKitchenRouteRequest extends FormRequest
{
    public function authorize(): bool
    {
        return true;
    }

    public function rules(): array
    {
        return [
            'branch_id' => ['required', 'string', 'max:40'],
            'kitchen_station_id' => ['required', 'string', 'max:40'],
        ];
    }
}

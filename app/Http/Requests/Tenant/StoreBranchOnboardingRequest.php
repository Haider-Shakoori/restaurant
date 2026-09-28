<?php

namespace App\Http\Requests\Tenant;

use App\Models\Restaurant;
use Illuminate\Validation\Rule;

class StoreBranchOnboardingRequest extends StoreBranchRequest
{
    public function rules(): array
    {
        $primaryBranch = Restaurant::where('profile_key', 'primary')
            ->first()
            ?->branches()
            ->where('is_primary', true)
            ->first();

        return [
            'code' => [
                'required',
                'string',
                'max:32',
                'regex:/^[A-Za-z0-9_-]+$/',
                Rule::unique('branches', 'code')->ignore($primaryBranch?->id),
            ],
            'name' => ['required', 'string', 'max:255'],
            'phone' => ['nullable', 'string', 'max:50'],
            'address' => ['nullable', 'string', 'max:255'],
        ];
    }
}

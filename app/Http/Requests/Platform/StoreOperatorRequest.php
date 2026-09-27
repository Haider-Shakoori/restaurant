<?php

namespace App\Http\Requests\Platform;

use App\Enums\PlatformRole;
use Illuminate\Foundation\Http\FormRequest;
use Illuminate\Validation\Rules\Enum;
use Illuminate\Validation\Rules\Password;

class StoreOperatorRequest extends FormRequest
{
    public function authorize(): bool
    {
        return $this->user()?->can('manage-operators') ?? false;
    }

    public function rules(): array
    {
        return [
            'name' => ['required', 'string', 'max:255'],
            'email' => ['required', 'email', 'max:255', 'unique:admin_users,email'],
            'role' => ['required', new Enum(PlatformRole::class)],
            'password' => ['required', 'confirmed', Password::min(10)->letters()->numbers()],
        ];
    }
}

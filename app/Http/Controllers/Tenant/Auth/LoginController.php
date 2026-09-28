<?php

namespace App\Http\Controllers\Tenant\Auth;

use App\Http\Controllers\Controller;
use App\Http\Requests\Tenant\LoginRequest;
use Illuminate\Http\RedirectResponse;
use Illuminate\Support\Facades\Auth;
use Illuminate\Support\Facades\RateLimiter;
use Illuminate\Support\Str;
use Illuminate\View\View;

class LoginController extends Controller
{
    public function create(): View|RedirectResponse
    {
        if (Auth::guard('tenant')->check()) {
            return redirect('/onboarding');
        }

        return view('tenant.auth.login');
    }

    public function store(LoginRequest $request): RedirectResponse
    {
        $key = tenant('id').'|'.Str::lower($request->string('email')).'|'.$request->ip();

        if (RateLimiter::tooManyAttempts($key, 5)) {
            return back()
                ->withInput($request->only('email'))
                ->withErrors(['email' => 'Too many login attempts. Try again shortly.']);
        }

        if (! Auth::guard('tenant')->attempt(
            $request->only('email', 'password'),
            $request->boolean('remember'),
        )) {
            RateLimiter::hit($key, 60);

            return back()
                ->withInput($request->only('email'))
                ->withErrors(['email' => 'The provided credentials are invalid.']);
        }

        if (! $request->user('tenant')->is_active) {
            Auth::guard('tenant')->logout();

            return back()->withErrors(['email' => 'Your restaurant account is inactive.']);
        }

        RateLimiter::clear($key);
        $request->session()->regenerate();

        return redirect()->intended('/onboarding');
    }

    public function destroy(): RedirectResponse
    {
        Auth::guard('tenant')->logout();

        request()->session()->invalidate();
        request()->session()->regenerateToken();

        return redirect('/login');
    }
}

<?php

namespace App\Http\Controllers\Platform\Auth;

use App\Http\Controllers\Controller;
use App\Http\Requests\Platform\LoginRequest;
use Illuminate\Http\RedirectResponse;
use Illuminate\Support\Facades\Auth;
use Illuminate\Support\Facades\RateLimiter;
use Illuminate\Support\Str;
use Illuminate\View\View;

class LoginController extends Controller
{
    public function create(): View|RedirectResponse
    {
        if (Auth::check()) {
            return redirect('/platform');
        }

        return view('platform.auth.login');
    }

    public function store(LoginRequest $request): RedirectResponse
    {
        $key = Str::lower($request->string('email')).'|'.$request->ip();

        if (RateLimiter::tooManyAttempts($key, 5)) {
            return back()
                ->withInput($request->only('email'))
                ->withErrors(['email' => 'Too many login attempts. Try again shortly.']);
        }

        if (! Auth::attempt(
            $request->only('email', 'password'),
            $request->boolean('remember'),
        )) {
            RateLimiter::hit($key, 60);

            return back()
                ->withInput($request->only('email'))
                ->withErrors(['email' => 'The provided credentials are invalid.']);
        }

        if (! $request->user()->is_active) {
            Auth::logout();

            return back()->withErrors(['email' => 'Your platform account is inactive.']);
        }

        RateLimiter::clear($key);
        $request->session()->regenerate();
        $request->user()->forceFill(['last_login_at' => now()])->save();

        return redirect()->intended('/platform');
    }

    public function destroy(): RedirectResponse
    {
        Auth::logout();

        request()->session()->invalidate();
        request()->session()->regenerateToken();

        return redirect('/platform/login');
    }
}

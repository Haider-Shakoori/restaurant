<?php

namespace App\Http\Controllers\Tenant;

use App\Http\Controllers\Controller;
use App\Models\MenuItem;
use Symfony\Component\HttpFoundation\StreamedResponse;
use Illuminate\Support\Facades\Storage;

class MenuItemImageController extends Controller
{
    public function __invoke(MenuItem $menuItem): StreamedResponse
    {
        abort_unless(filled($menuItem->image_path), 404);

        $disk = Storage::disk('public');
        abort_unless($disk->exists($menuItem->image_path), 404);

        return $disk->response(
            $menuItem->image_path,
            null,
            [
                'Cache-Control' => 'public, max-age=86400',
                'X-Content-Type-Options' => 'nosniff',
            ],
        );
    }
}

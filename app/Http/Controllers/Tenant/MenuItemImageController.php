<?php

namespace App\Http\Controllers\Tenant;

use App\Http\Controllers\Controller;
use App\Models\MenuItem;
use Illuminate\Support\Facades\Storage;
use Symfony\Component\HttpFoundation\StreamedResponse;

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
                // Menu image URLs are content-versioned (?v=); a replaced image
                // changes the path and URL, so old bytes stay cached safely.
                'Cache-Control' => 'public, max-age=31536000, immutable',
                'ETag' => '"'.substr(sha1($menuItem->image_path), 0, 20).'"',
                'X-Content-Type-Options' => 'nosniff',
            ],
        );
    }
}

@extends('tenant.layouts.app')

@section('title', 'Menu')
@section('heading', 'Menu')
@section('subheading', 'Categories, pricing and item availability.')

@section('content')
    <div class="grid gap-6 lg:grid-cols-[1fr_22rem]">
        <div class="space-y-5">
            @forelse ($categories as $category)
                <section class="overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-sm">
                    <div class="border-b border-slate-200 px-5 py-4">
                        <div class="flex items-center justify-between gap-3">
                            <h2 class="text-lg font-black">{{ $category->name }}</h2>
                            <span class="text-xs text-slate-500">{{ $category->items->count() }} items</span>
                        </div>
                    </div>
                    <div class="grid gap-3 p-5 md:grid-cols-2 xl:grid-cols-3">
                        @forelse ($category->items as $item)
                            <article class="overflow-hidden rounded-xl border border-slate-200 bg-white">
                                <div class="aspect-[4/3] bg-slate-100">
                                    @if ($item->image_url)
                                        <img src="{{ $item->image_url }}" alt="{{ $item->name }}" loading="lazy" class="h-full w-full object-cover">
                                    @else
                                        <div class="flex h-full items-center justify-center text-center text-sm font-semibold text-slate-400">
                                            No menu image
                                        </div>
                                    @endif
                                </div>
                                <div class="p-4">
                                <div class="flex items-start justify-between gap-4">
                                    <div>
                                        <h3 class="font-bold">{{ $item->name }}</h3>
                                        <p class="mt-1 text-xs text-slate-500">{{ $item->sku ?: 'No SKU' }}</p>
                                    </div>
                                    <span class="whitespace-nowrap font-black">{{ number_format((float) $item->price, 0) }} AFN</span>
                                </div>
                                @if ($item->description)
                                    <p class="mt-3 text-sm leading-6 text-slate-500">{{ $item->description }}</p>
                                @endif
                                <p class="mt-3 text-xs font-bold {{ $item->is_available ? 'text-emerald-700' : 'text-red-600' }}">{{ $item->is_available ? 'Available' : 'Unavailable' }}</p>
                                @if (in_array($currentUser->role, ['owner','admin','manager'], true))
                                    <form method="POST" action="/setup/menu/item/{{ $item->id }}/image" enctype="multipart/form-data" class="mt-3 flex items-center gap-2">
                                        @csrf
                                        <input type="file" name="image" accept="image/jpeg,image/png,image/webp" required class="min-w-0 flex-1 text-xs text-slate-500 file:mr-2 file:rounded-lg file:border-0 file:bg-slate-100 file:px-2 file:py-1.5 file:text-xs file:font-bold file:text-slate-700">
                                        <button class="rounded-lg bg-slate-900 px-3 py-2 text-xs font-bold text-white">{{ $item->image_url ? 'Replace' : 'Upload' }}</button>
                                    </form>
                                @endif
                                </div>
                            </article>
                        @empty
                            <p class="text-sm text-slate-500">No items in this category.</p>
                        @endforelse
                    </div>
                </section>
            @empty
                <div class="rounded-2xl border border-dashed border-slate-300 bg-white p-10 text-center text-slate-500">No menu categories yet.</div>
            @endforelse
        </div>

        @if (in_array($currentUser->role, ['owner','admin','manager'], true))
            <aside class="space-y-5">
                <section class="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
                    <h2 class="font-black">Add category</h2>
                    <form method="POST" action="/setup/menu/category" class="mt-4 space-y-3">
                        @csrf
                        <input name="name" required placeholder="Category name" class="w-full rounded-xl border border-slate-300 px-3 py-2.5 text-sm">
                        <button class="w-full rounded-xl bg-slate-900 px-4 py-2.5 text-sm font-bold text-white">Add category</button>
                    </form>
                </section>

                <section class="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
                    <h2 class="font-black">Add menu item</h2>
                    <form method="POST" action="/setup/menu/item" enctype="multipart/form-data" class="mt-4 space-y-3">
                        @csrf
                        <select name="menu_category_id" class="w-full rounded-xl border border-slate-300 px-3 py-2.5 text-sm">
                            <option value="">Uncategorized</option>
                            @foreach ($categories as $category)<option value="{{ $category->id }}">{{ $category->name }}</option>@endforeach
                        </select>
                        <input name="sku" placeholder="SKU (optional)" class="w-full rounded-xl border border-slate-300 px-3 py-2.5 text-sm">
                        <input name="name" required placeholder="Item name" class="w-full rounded-xl border border-slate-300 px-3 py-2.5 text-sm">
                        <textarea name="description" rows="2" placeholder="Description" class="w-full rounded-xl border border-slate-300 px-3 py-2.5 text-sm"></textarea>
                        <label class="block text-xs font-bold text-slate-600">
                            Menu image
                            <input type="file" name="image" accept="image/jpeg,image/png,image/webp" class="mt-1 block w-full rounded-xl border border-slate-300 px-3 py-2.5 text-sm">
                        </label>
                        <input name="price" required type="number" min="0" step="0.01" placeholder="Price AFN" class="w-full rounded-xl border border-slate-300 px-3 py-2.5 text-sm">
                        <button class="w-full rounded-xl bg-emerald-500 px-4 py-2.5 text-sm font-black text-slate-950">Add item</button>
                    </form>
                </section>
            </aside>
        @endif
    </div>
@endsection

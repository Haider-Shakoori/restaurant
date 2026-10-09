# Afghan demo menu — tenant-scoped installation

The new **opt-in** installer seeds one existing restaurant with 60 Afghan dishes in 7 categories, English/Dari/Pashto names, illustrative AFN prices, preparation times and branch-specific KOT routes. It uses 29 generated food photos and leaves 31 menu records without images until those are generated. A 30th photo (Baklava) is an unused bonus.

**Important:** No demo menu is installed automatically when a new business is provisioned. The existing platform, tenants, customers, orders and financials are untouched until this explicitly targeted installer is run.

## Installation

1. Back up the target restaurant's tenant database and confirm its exact tenant ID.
2. Download `Afghan_Restaurant_Menu_Images_Batches_01_02_03_Combined.zip` from the ChatGPT conversation and upload it **unchanged** to the restaurant VPS (example: `/srv/imports/afghan-menu.zip`). The ZIP itself is **not committed to GitHub**.
3. Deploy this branch after checks pass and run the tenant-specific migration:

```bash
php artisan tenants:migrate --tenants=<EXISTING_TENANT_ID>
```

4. Perform a non-mutating ZIP preflight:

```bash
php artisan restaurant:seed-afghan-menu --tenant=<EXISTING_TENANT_ID> --archive=/srv/imports/afghan-menu.zip --dry-run
```

5. Install the images and menu for that tenant only:

```bash
php artisan restaurant:seed-afghan-menu --tenant=<EXISTING_TENANT_ID> --archive=/srv/imports/afghan-menu.zip
```

The installer validates the WebP entries and refuses to overwrite conflicting files or unrelated menu SKUs. Repeated runs preserve existing menu prices and branch kitchen routes.

## Actual image paths

The archive contains `storage/app/public/menu-items/kabuli-pulao.webp`. The command reads it from the ZIP and copies it into **that tenant's public disk** at `menu-items/afghan-sample/kabuli-pulao.webp`, which is stored in `menu_items.image_path`. The existing restaurant application serves the image via **`/media/menu-items/{menu_item_ulid}`**, not via a hardcoded `/storage/` URL. The tenancy filesystem bootstrapping means that simply copying images into the central Laravel `storage/app/public` is incorrect.

Check that `Storage::disk('public')->exists('menu-items/afghan-sample/kabuli-pulao.webp')` returns true **with the right tenant initialized**, confirm the image endpoint returns `image/webp`, and verify KOT routing in that restaurant.

All prices are illustrative demo values and the photos are AI-generated menu illustrations. Review prices and dish appearance before enabling public sales.

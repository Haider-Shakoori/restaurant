import 'package:flutter/material.dart';

import '../../app/app_theme.dart';

/// A horizontally scrollable category selector shared by the menu browser and
/// order-entry catalog. It stays above the photo grid on phones and tablets.
class MenuCategoryTopBar extends StatelessWidget {
  const MenuCategoryTopBar({
    required this.categories,
    required this.selectedCategoryId,
    required this.onSelected,
    super.key,
  });

  final List<Map<String, Object?>> categories;
  final String? selectedCategoryId;
  final ValueChanged<String?> onSelected;

  @override
  Widget build(BuildContext context) {
    return SizedBox(
      height: 58,
      child: ColoredBox(
        color: RestaurantColors.appBackground,
        child: ListView.separated(
          key: const Key('menu-category-topbar'),
          scrollDirection: Axis.horizontal,
          padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 7),
          itemCount: categories.length + 1,
          separatorBuilder: (context, index) => const SizedBox(width: 8),
          itemBuilder: (context, index) {
            final category = index == 0 ? null : categories[index - 1];
            final id = category?['id']?.toString();
            final name = category?['name']?.toString() ?? 'All items';
            final selected = selectedCategoryId == id;

            return ChoiceChip(
              key: ValueKey<String>('menu-category-${id ?? 'all'}'),
              selected: selected,
              showCheckmark: false,
              onSelected: (_) => onSelected(id),
              label: ConstrainedBox(
                constraints: const BoxConstraints(maxWidth: 180),
                child: Text(
                  name,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                ),
              ),
              selectedColor: RestaurantColors.brandSoft,
              backgroundColor: RestaurantColors.surface,
              side: BorderSide(
                color: selected
                    ? RestaurantColors.brand
                    : RestaurantColors.borderStrong,
              ),
              labelStyle: TextStyle(
                fontWeight: FontWeight.w700,
                color: selected
                    ? RestaurantColors.brand
                    : RestaurantColors.textSecondary,
              ),
            );
          },
        ),
      ),
    );
  }
}

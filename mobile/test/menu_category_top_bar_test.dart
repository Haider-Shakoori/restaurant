import 'package:businessos_restaurant_waiter/features/orders/menu_category_top_bar.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  testWidgets('category bar replaces the rail with horizontal navigation',
      (tester) async {
    final categories = <Map<String, Object?>>[
      {'id': 'starters', 'name': 'Starters'},
      {'id': 'drinks', 'name': 'Drinks'},
      {'id': 'mains', 'name': 'Main dishes'},
      {'id': 'desserts', 'name': 'Desserts'},
    ];
    String? selected;

    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: Align(
            alignment: Alignment.topLeft,
            child: SizedBox(
              width: 320,
              child: StatefulBuilder(
                builder: (context, setState) => MenuCategoryTopBar(
                  categories: categories,
                  selectedCategoryId: selected,
                  onSelected: (id) => setState(() => selected = id),
                ),
              ),
            ),
          ),
        ),
      ),
    );

    final bar = find.byKey(const Key('menu-category-topbar'));
    expect(tester.widget<ListView>(bar).scrollDirection, Axis.horizontal);
    expect(find.byKey(const Key('menu-category-all')), findsOneWidget);

    await tester.tap(find.text('Starters'));
    await tester.pump();
    expect(selected, 'starters');
    expect(
      tester.widget<ChoiceChip>(
        find.byKey(const Key('menu-category-starters')),
      ).selected,
      isTrue,
    );

    await tester.drag(bar, const Offset(-280, 0));
    await tester.pumpAndSettle();
    expect(find.text('Main dishes'), findsOneWidget);
    expect(tester.takeException(), isNull);
  });

  testWidgets('category bar supports RTL and filtering back to all items',
      (tester) async {
    String? selected = 'drinks';
    await tester.pumpWidget(
      MaterialApp(
        home: Directionality(
          textDirection: TextDirection.rtl,
          child: Scaffold(
            body: StatefulBuilder(
              builder: (context, setState) => MenuCategoryTopBar(
                categories: const [
                  {'id': 'drinks', 'name': 'نوشیدنی‌ها'},
                ],
                selectedCategoryId: selected,
                onSelected: (id) => setState(() => selected = id),
              ),
            ),
          ),
        ),
      ),
    );

    final bar = find.byKey(const Key('menu-category-topbar'));
    expect(tester.widget<ListView>(bar).scrollDirection, Axis.horizontal);
    expect(
      tester.widget<ChoiceChip>(
        find.byKey(const Key('menu-category-drinks')),
      ).selected,
      isTrue,
    );

    await tester.tap(find.text('All items'));
    await tester.pump();
    expect(selected, isNull);
    expect(
      tester.widget<ChoiceChip>(
        find.byKey(const Key('menu-category-all')),
      ).selected,
      isTrue,
    );
    expect(tester.takeException(), isNull);
  });
}

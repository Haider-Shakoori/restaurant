import 'package:businessos_restaurant_waiter/features/orders/menu_item_photo.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  testWidgets('a menu item without a server photo shows an honest placeholder',
      (tester) async {
    await tester.pumpWidget(
      const MaterialApp(
        home: Scaffold(
          body: SizedBox(
            width: 230,
            height: 180,
            child: MenuItemPhoto(imageUrl: null, credentials: null),
          ),
        ),
      ),
    );
    expect(find.text('No photo uploaded'), findsOneWidget);
    expect(find.byIcon(Icons.restaurant_menu_rounded), findsOneWidget);
  });

  testWidgets('an unsafe relative image path is not requested', (tester) async {
    await tester.pumpWidget(
      const MaterialApp(
        home: Scaffold(
          body: MenuItemPhoto(
            imageUrl: '/private/credentials.txt',
            credentials: null,
          ),
        ),
      ),
    );
    expect(find.text('No photo uploaded'), findsOneWidget);
    expect(find.byType(Image), findsNothing);
  });
}

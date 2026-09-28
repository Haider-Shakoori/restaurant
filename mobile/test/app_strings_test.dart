import 'package:businessos_restaurant_waiter/app/app_strings.dart';
import 'package:flutter/widgets.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('Dari and Pashto use RTL while English remains LTR', () {
    expect(AppStrings(const Locale('en')).isRtl, isFalse);
    expect(AppStrings(const Locale('fa')).isRtl, isTrue);
    expect(AppStrings(const Locale('ps')).isRtl, isTrue);
  });
}

import 'package:businessos_restaurant_waiter/features/orders/quick_add_policy.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('unmodified standard items can be quick-added', () {
    expect(canQuickAddMenuItem(<String, Object?>{
      'id': 'QABULI', 'name': 'Qabuli',
    }), true);
    expect(canQuickAddMenuItem(<String, Object?>{
      'modifier_groups_json': '[]',
    }), true);
    expect(canQuickAddMenuItem(<String, Object?>{
      'modifier_groups_json': '',
    }), true);
  });

  test('optional and required modifiers both open the detail dialog', () {
    expect(canQuickAddMenuItem(<String, Object?>{
      'modifier_groups_json':
          '[{"id":"side","min_selections":0,"max_selections":1}]',
    }), false);
    expect(canQuickAddMenuItem(<String, Object?>{
      'modifier_groups_json':
          '[{"id":"rice","min_selections":1,"max_selections":1}]',
    }), false);
  });

  test('malformed or unknown modifiers never skip guest options', () {
    expect(canQuickAddMenuItem(<String, Object?>{
      'modifier_groups_json': '{broken',
    }), false);
    expect(canQuickAddMenuItem(<String, Object?>{
      'modifier_groups_json': '{}',
    }), false);
    expect(canQuickAddMenuItem(<String, Object?>{
      'modifier_groups_json': 10,
    }), false);
  });
}

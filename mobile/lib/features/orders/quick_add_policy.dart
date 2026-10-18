import 'dart:convert';

/// A one-tap waiter action must never bypass a modifier selection, including
/// optional modifiers that the guest may want to review. Any malformed or
/// unknown modifier configuration uses the full options dialog instead.
bool canQuickAddMenuItem(Map<String, Object?> menuItem) {
  final raw = menuItem['modifier_groups_json'];
  if (raw == null) return true;
  if (raw is String && raw.trim().isEmpty) return true;
  try {
    final decoded = raw is String ? jsonDecode(raw) : raw;
    return decoded is List && decoded.isEmpty;
  } on FormatException {
    return false;
  } on TypeError {
    return false;
  }
}

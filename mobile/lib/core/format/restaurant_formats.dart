/// Shared presentation formats that mirror the desktop POS so a waiter reading
/// the phone and an operator reading Windows see identical money, order-number,
/// status and date text.
///
/// Desktop reference strings:
/// * money  -> `AFN {value:N2}` (thousands grouped, 2 decimals, prefix)
/// * number -> `#{DisplayNumber}` (short daily number, no zero padding)
/// * status -> uppercase tokens (`READY`, `SUBMITTED_PENDING_SYNC`)
/// * dates  -> `dd MMM yyyy`, times `HH:mm` (invariant English month names)
library;

const List<String> _monthsShort = <String>[
  'Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun',
  'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec',
];

/// Formats a monetary amount exactly like desktop `AFN {value:N2}`.
/// Defaults to 2 decimals; pass `decimals: 0` for the compact `N0` tiles.
String formatMoney(Object? value, {int decimals = 2}) {
  final amount = _toDouble(value) ?? 0;
  final negative = amount < 0;
  final fixed = amount.abs().toStringAsFixed(decimals);
  final parts = fixed.split('.');
  final grouped = _groupThousands(parts[0]);
  final text = parts.length > 1 ? '$grouped.${parts[1]}' : grouped;
  return 'AFN ${negative ? '-' : ''}$text';
}

/// Formats the short human order number as `#n` (never zero padded), falling
/// back to the supplied value when no display number has been assigned yet.
String formatOrderNumber(Object? displayNumber, {String? fallback}) {
  final parsed = displayNumber is num
      ? displayNumber.toInt()
      : int.tryParse(displayNumber?.toString().trim() ?? '');
  if (parsed != null && parsed > 0) {
    return '#$parsed';
  }
  return fallback ?? '';
}

/// Renders a raw status token the way the desktop shows it: uppercase with
/// underscores replaced by spaces (`submitted_pending_sync` -> `SUBMITTED PENDING SYNC`).
String formatStatus(Object? raw) {
  final value = raw?.toString().trim() ?? '';
  if (value.isEmpty) {
    return '';
  }
  return value.replaceAll('_', ' ').toUpperCase();
}

/// Renders a table state the way the desktop floor view does: title case with
/// spaces instead of underscores (`out_for_cleaning` -> `Out For Cleaning`).
String formatTableStatus(Object? raw) {
  final value = raw?.toString().replaceAll('_', ' ').trim() ?? '';
  if (value.isEmpty) {
    return '';
  }
  return value
      .split(RegExp(r'\s+'))
      .where((word) => word.isNotEmpty)
      .map((word) => word[0].toUpperCase() + word.substring(1).toLowerCase())
      .join(' ');
}

/// `13 Oct 2026` (desktop `dd MMM yyyy`), rendered in the device's local time.
String formatDate(DateTime value) {
  final local = value.toLocal();
  return '${_two(local.day)} ${_monthsShort[local.month - 1]} ${local.year}';
}

/// `09:05` (desktop `HH:mm`), rendered in the device's local time.
String formatTime(DateTime value) {
  final local = value.toLocal();
  return '${_two(local.hour)}:${_two(local.minute)}';
}

/// `13 Oct 2026 09:05` (desktop `dd MMM yyyy HH:mm`).
String formatDateTime(DateTime value) => '${formatDate(value)} ${formatTime(value)}';

String _two(int value) => value.toString().padLeft(2, '0');

String _groupThousands(String digits) {
  final buffer = StringBuffer();
  final length = digits.length;
  for (var i = 0; i < length; i++) {
    if (i > 0 && (length - i) % 3 == 0) {
      buffer.write(',');
    }
    buffer.write(digits[i]);
  }
  return buffer.toString();
}

double? _toDouble(Object? value) {
  if (value == null) {
    return null;
  }
  if (value is num) {
    return value.toDouble();
  }
  return double.tryParse(value.toString().replaceAll(',', '').trim());
}
